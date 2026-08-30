using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using HonestFlow.Application.Core;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Installation.Planning;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Models;

namespace HonestFlow.UI;

public partial class InstallationModeWindow : Window
{
    private readonly ServiceInstallationSession _session;
    private readonly HttpClient _httpClient;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ComponentInstallationWorkflow _workflow;
    private readonly IPData _installationTarget;
    private readonly ObservableCollection<ComponentRow> _rows;
    private bool _busy;

    public InstallationModeWindow(
        ServiceInstallationSession session,
        HttpClient httpClient,
        ILogService logService)
    {
        InitializeComponent();
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        var source = new ApiInstallationPackageSource(_httpClient, _session);
        var progress = new WindowProgress(this);
        var dialogs = new WindowDialogs(this);
        var service = new InstallationService(
            logService,
            progress,
            dialogs,
            new InstallationOnlyOperationGuard(_session),
            useRemoteConfigMode: true,
            packageSource: source,
            initializeLm: false);
        _workflow = new ComponentInstallationWorkflow(service);
        _installationTarget = new IPData
        {
            Name = "Service installation",
            Architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86",
            Versions = source.Versions
        };
        _rows = new ObservableCollection<ComponentRow>(_session.Packages.Select(package => new ComponentRow
        {
            IsSelected = true,
            Component = package.Component,
            Name = DisplayName(package.Component),
            Version = package.Version ?? "Не задана",
            FileName = package.FileName ?? "Файл не настроен"
        }));
        ComponentsGrid.ItemsSource = _rows;
    }

    private async void Install_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        ComponentOperationReadiness readiness = _workflow.CheckReadiness(_installationTarget, checkLmRequirements: true);
        EnsureReady(readiness);
        return await _workflow.InstallAsync(_installationTarget, _lifetime.Token);
    });

    private async void Reinstall_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        InstallationComponent[] selected = _rows.Where(row => row.IsSelected)
            .Select(row => row.Component).ToArray();
        if (selected.Length == 0) throw new InvalidOperationException("Выберите хотя бы один компонент.");
        ComponentOperationReadiness readiness = _workflow.CheckReadiness(_installationTarget, checkLmRequirements: false);
        EnsureReady(readiness);
        return await _workflow.ReinstallAsync(_installationTarget, selected);
    });

    private async Task RunAsync(Func<Task<bool>> action)
    {
        if (_busy) return;
        _busy = true;
        InstallButton.IsEnabled = ReinstallButton.IsEnabled = false;
        try
        {
            bool result = await action();
            StatusText.Text = result
                ? "Операция завершена успешно."
                : "Операция завершена без подтверждения успеха. Проверьте журнал.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally
        {
            _busy = false;
            InstallButton.IsEnabled = ReinstallButton.IsEnabled = _session.IsActive;
        }
    }

    private static void EnsureReady(ComponentOperationReadiness readiness)
    {
        if (readiness.CanContinue) return;
        throw new InvalidOperationException(readiness.Status == ComponentOperationReadinessStatus.AdministratorRequired
            ? "Запустите HonestFlow от имени администратора."
            : "Не удалось подготовить режим установки.");
    }

    private void Return_Click(object sender, RoutedEventArgs e)
    {
        _session.Dispose();
        var startup = new StartupWindow();
        System.Windows.Application.Current.MainWindow = startup;
        startup.Show();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _lifetime.Cancel();
        _session.Dispose();
        _httpClient.Dispose();
        _lifetime.Dispose();
        base.OnClosed(e);
    }

    private static string DisplayName(InstallationComponent component) => component switch
    {
        InstallationComponent.LmModule => "ЛМ ЧЗ",
        InstallationComponent.AtolDriver => "Драйвер АТОЛ",
        InstallationComponent.Esm => "ТС ПИоТ / ESM",
        InstallationComponent.Controller => "ЛМ Контроллер",
        _ => component.ToString()
    };

    private sealed class ComponentRow
    {
        public bool IsSelected { get; set; }
        public InstallationComponent Component { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
    }

    private sealed class WindowProgress : IProgressService
    {
        private readonly InstallationModeWindow _window;
        public WindowProgress(InstallationModeWindow window) => _window = window;
        public void SetProgress(int percent, string stepName) => _window.Dispatcher.Invoke(() =>
        {
            _window.OperationProgress.Value = Math.Clamp(percent, 0, 100);
            _window.StatusText.Text = stepName;
        });
    }

    private sealed class WindowDialogs : IUserDialogService
    {
        private readonly Window _owner;
        public WindowDialogs(Window owner) => _owner = owner;
        public void ShowInformation(string message, string title) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        public void ShowWarning(string message, string title) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        public void ShowError(string message, string title) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        public bool Confirm(string message, string title, UserDialogIcon icon = UserDialogIcon.Warning) =>
            Show(message, title, MessageBoxButton.YesNo,
                icon == UserDialogIcon.Error ? MessageBoxImage.Error : MessageBoxImage.Warning) == MessageBoxResult.Yes;
        private MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage icon) =>
            _owner.Dispatcher.Invoke(() => MessageBox.Show(_owner, message, title, buttons, icon));
    }
}
