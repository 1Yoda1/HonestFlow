using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using HonestFlow.Application.Bootstrap;
using HonestFlow.Application.Core;
using HonestFlow.Application.DeviceIdentity;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.Lm;
using HonestFlow.Application.PointStatus;
using HonestFlow.Application.RemoteAccess;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Infrastructure.DeviceIdentity;
using HonestFlow.Infrastructure.Licensing;
using HonestFlow.Models;
using HonestFlow.Models.Licensing;

namespace HonestFlow.WpfPrototype;

public partial class CompactMainWindow : Window
{
    private readonly ApplicationStartupSession _session;
    private readonly IPData _client;
    private LicenseObservationSnapshot? _license;
    private readonly ApplicationStartupController _startupController = new();
    private readonly PointStatusRefreshService _pointStatusRefresh;
    private readonly AutoFixWorkflow _autoFixWorkflow;
    private readonly ApiServerConnectivityProbe? _apiServerConnectivityProbe;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _visibleLifetime;
    private DiagnosticsSnapshot? _lastDiagnostics;
    private HonestFlowCloudStatus _lastCloudStatus = HonestFlowCloudStatus.Unknown;
    private string? _deviceId;
    private MainWindow? _fullWindow;
    private bool _operationRunning;
    private bool _returningToStartup;
    private CancellationTokenSource? _autoFixCancellation;
    private TaskCompletionSource? _autoFixCompletion;

    public CompactMainWindow(ApplicationStartupSession session, IPData client, LicenseObservationSnapshot? license)
    {
        InitializeComponent();
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _license = license;

        var ruDesktop = new RuDesktopService(_session.LogService);
        var pointStatus = new PointStatusService(
            _session.Startup.UseRemoteConfigMode,
            _session.Startup.Ips?.Count ?? _session.Startup.RemoteIps?.Count ?? 0,
            _session.Startup.Ips ?? _session.Startup.RemoteIps,
            ruDesktop);
        _pointStatusRefresh = new PointStatusRefreshService(
            pointStatus,
            new ComponentVersionStatusService(_session.LogService),
            new PointStatusReportBuilder(),
            _session.LogService);
        _autoFixWorkflow = WpfAutoFixComposition.Create(
            this,
            _session.Startup,
            _client,
            _session.LogService,
            _pointStatusRefresh,
            new CompactAutoFixProgress(this),
            ResolveInstallationOptions,
            CreateLicenseGuard,
            ApplyAutoFixRefresh);
        if (_session.Startup.AuthService is IApiSessionProvider apiSessionProvider)
            _apiServerConnectivityProbe = new ApiServerConnectivityProbe(apiSessionProvider.ApiSessionService);

        Loaded += async (_, _) => await StartVisibleWorkAsync();
        LicenseObservationSnapshotStore.Instance.SnapshotChanged += LicenseSnapshotChanged;
    }

    private async Task StartVisibleWorkAsync()
    {
        StopVisibleWork();
        _visibleLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        await LoadDeviceIdAsync(_visibleLifetime.Token);
        await RefreshStatusAsync(_visibleLifetime.Token);
        _ = RunPeriodicLicenseRefreshAsync(_visibleLifetime.Token);
    }

    private void StopVisibleWork()
    {
        _visibleLifetime?.Cancel();
        _visibleLifetime?.Dispose();
        _visibleLifetime = null;
    }

    private async Task LoadDeviceIdAsync(CancellationToken cancellationToken)
    {
        _deviceId = _license?.DeviceId;
        if (string.IsNullOrWhiteSpace(_deviceId))
        {
            DeviceIdentityResult identity = await new FileDeviceIdentityService(
                    new DpapiDeviceIdentityStateProtector())
                .GetOrCreateAsync(cancellationToken);
            _deviceId = identity.IsAvailable ? identity.DeviceId : null;
        }

        DeviceIdText.Text = string.IsNullOrWhiteSpace(_deviceId) ? "Недоступен" : _deviceId;
        CopyDeviceIdButton.IsEnabled = !string.IsNullOrWhiteSpace(_deviceId);
    }

    private async Task RefreshStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            Task<PointStatusRefreshResult> refreshTask = _pointStatusRefresh.RefreshAsync(
                _client,
                _session.Startup.RemoteVersions,
                includeLicensedComponents: true,
                cancellationToken);
            Task<HonestFlowCloudStatus> cloudStatusTask = _apiServerConnectivityProbe is null
                ? Task.FromResult(HonestFlowCloudStatus.Unknown)
                : _apiServerConnectivityProbe.CheckAsync(cancellationToken);
            await Task.WhenAll(refreshTask, cloudStatusTask);

            PointStatusRefreshResult refresh = await refreshTask;
            _lastDiagnostics = refresh.Diagnostics;
            _lastCloudStatus = await cloudStatusTask;
            ApplyPresentation(CompactWorkplacePresentation.Create(
                refresh.Diagnostics,
                _lastCloudStatus));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "WPF compact diagnostics refresh failed", nameof(CompactMainWindow));
            StatusTitleText.Text = "Работа не подтверждена";
            StatusDescriptionText.Text = "Не удалось подтвердить готовность рабочего места. Повторите проверку позже.";
            CloudStatusText.Text = "Облако: состояние неизвестно";
            LastCheckText.Text = "Последняя проверка: недоступна";
            ApplyVisualState("#C66A12", "#FFF5EA", "#F2CEA8");
            AutoFixButton.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyPresentation(CompactWorkplacePresentation presentation)
    {
        StatusTitleText.Text = presentation.Title;
        StatusDescriptionText.Text = presentation.Description;
        CloudStatusText.Text = presentation.CloudStatus;
        LastCheckText.Text = presentation.CheckedAtUtc is { } checkedAt
            ? "Последняя проверка: " + checkedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
            : "Последняя проверка: ещё не выполнялась";
        ApplyVisualState(presentation.AccentColor, presentation.TintColor, presentation.BorderColor);
        AutoFixButton.Visibility = presentation.CanAutoFix ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyVisualState(string accentColor, string tintColor, string borderColor)
    {
        StatusIndicator.Background = Brush(accentColor);
        StatusTitleText.Foreground = Brush(accentColor);
        StatusCard.Background = Brush(tintColor);
        StatusCard.BorderBrush = Brush(borderColor);
    }

    private static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromString(color);

    private async Task RunPeriodicLicenseRefreshAsync(CancellationToken cancellationToken)
    {
        var workflow = new LicenseRefreshWorkflow(_session.Startup.AuthService, _session.LogService);
        await workflow.RunPeriodicAsync(() => _client, cancellationToken);
    }

    private async void AutoFix_Click(object sender, RoutedEventArgs e)
    {
        if (_lastDiagnostics is null || _lastDiagnostics.WorkState is not (WorkState.Attention or WorkState.WorkImpossible))
            return;

        MessageBoxResult confirmation = MessageBox.Show(this,
            "HonestFlow сформирует план установки и может изменить компоненты рабочего места. Продолжить?",
            "Исправить автоматически",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
            return;

        await RunAutoFixAsync();
    }

    private async void Help_Click(object sender, RoutedEventArgs e)
    {
        if (_license is null)
            return;
        await RunOperationAsync("Отправляем запрос помощи…", async () =>
        {
            await _startupController.SendHelpRequestAsync(_session, _license, _lifetime.Token);
            OperationText.Text = "Запрос помощи отправлен.";
        });
    }

    private async void OpenFull_Click(object sender, RoutedEventArgs e) => await OpenFullWindowAsync();

    private async Task OpenFullWindowAsync()
    {
        if (_fullWindow is not null || _returningToStartup)
            return;

        StopVisibleWork();
        Hide();
        var fullWindow = new MainWindow(_session.Startup, _client, _license)
        {
            Owner = this,
            WindowState = WindowState.Maximized
        };
        _fullWindow = fullWindow;
        fullWindow.Closed += FullWindow_Closed;
        System.Windows.Application.Current.MainWindow = fullWindow;
        fullWindow.Show();
        await Task.CompletedTask;
    }

    private async Task RunAutoFixAsync()
    {
        if (_operationRunning) return;
        _operationRunning = true;
        AutoFixButton.IsEnabled = HelpButton.IsEnabled = OpenFullButton.IsEnabled = false;
        _autoFixCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        ShowAutoFixProgress();
        try
        {
            AutoFixResult result = await _autoFixWorkflow.RunAsync(SetAutoFixProgress, _autoFixCancellation.Token);
            while (result.Status == AutoFixStatus.RequiresConfirmation && result.Continuation != null)
            {
                bool confirmed = MessageBox.Show(this,
                    $"ЛМ ЧЗ настроен для другой организации.\n\nВы точно работаете под «{_client.Name ?? "текущий клиент"}» на этом рабочем месте?\n\n" +
                    "При подтверждении будет переустановлен только ЛМ ЧЗ.",
                    "Подтверждение организации ЛМ ЧЗ",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) == MessageBoxResult.Yes;
                result = await _autoFixWorkflow.ContinueAsync(
                    result.Continuation, confirmed, SetAutoFixProgress, _autoFixCancellation.Token);
            }

            string finalMessage = WpfAutoFixComposition.ResultMessage(result);
            OperationText.Text = finalMessage;
            CompactAutoFixCurrentText.Text = finalMessage;
            CompactAutoFixProgressBar.IsIndeterminate = false;
            CompactAutoFixProgressBar.Value = result.Status is AutoFixStatus.Success or AutoFixStatus.NoFixNeeded ? 100 : 0;
            if (!_lifetime.IsCancellationRequested)
                await WaitForAutoFixAcknowledgementAsync();
        }
        catch (OperationCanceledException) when (_autoFixCancellation.IsCancellationRequested)
        {
            OperationText.Text = "Автоматическое исправление отменено.";
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "WPF compact AutoFix failed", nameof(CompactMainWindow));
            OperationText.Text = "Не удалось выполнить автоматическое исправление: " + ex.Message;
        }
        finally
        {
            _autoFixCompletion = null;
            HideAutoFixProgress();
            _autoFixCancellation.Dispose();
            _autoFixCancellation = null;
            _operationRunning = false;
            AutoFixButton.IsEnabled = HelpButton.IsEnabled = OpenFullButton.IsEnabled = true;
        }
    }

    private void ApplyAutoFixRefresh(PointStatusRefreshResult refresh)
    {
        _lastDiagnostics = refresh.Diagnostics;
        ApplyPresentation(CompactWorkplacePresentation.Create(refresh.Diagnostics, _lastCloudStatus));
    }

    private InstallationOptions? ResolveInstallationOptions(LmSystemRequirementsResult? requirements)
    {
        if (requirements?.MeetsMinimum != false)
            return InstallationOptions.Default;
        string warnings = string.Join(Environment.NewLine, requirements.MinimumWarnings.Select(item => "• " + item));
        MessageBoxResult choice = MessageBox.Show(this,
            "Минимальные системные требования ЛМ ЧЗ не соблюдены:\n\n" + warnings +
            "\n\nДа — продолжить полную установку.\nНет — пропустить ЛМ ЧЗ и Контроллер.\nОтмена — не начинать установку.",
            "Требования ЛМ ЧЗ", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        return choice switch
        {
            MessageBoxResult.Yes => InstallationOptions.Default,
            MessageBoxResult.No => new InstallationOptions { SkipLmStack = true },
            _ => null
        };
    }

    private ILicenseOperationGuard CreateLicenseGuard() => new LicenseOperationGuard(new LicenseAccessPolicy(
        LicenseRuntimeConfiguration.FromEnvironment().EnforcementMode,
        LicenseObservationSnapshotStore.Instance,
        () => _client.ClientId));

    private void ShowAutoFixProgress()
    {
        CompactAutoFixProgressBar.IsIndeterminate = true;
        CompactAutoFixProgressBar.Value = 0;
        CompactAutoFixCurrentText.Text = "Проверяем состояние…";
        CompactAutoFixHistoryList.ItemsSource = null;
        CompactAutoFixHistoryPanel.Visibility = Visibility.Collapsed;
        CompactAutoFixCancelButton.Content = "Отменить";
        CompactAutoFixCancelButton.IsEnabled = true;
        CompactAutoFixOverlay.Visibility = Visibility.Visible;
    }

    private void HideAutoFixProgress()
    {
        CompactAutoFixOverlay.Visibility = Visibility.Collapsed;
        CompactAutoFixProgressBar.IsIndeterminate = false;
        CompactAutoFixHistoryList.ItemsSource = null;
        CompactAutoFixHistoryPanel.Visibility = Visibility.Collapsed;
    }

    private void SetAutoFixProgress(AutoFixProgress progress)
    {
        Dispatcher.Invoke(() =>
        {
            CompactAutoFixProgressBar.IsIndeterminate = true;
            CompactAutoFixCurrentText.Text = progress.CurrentStep;
            CompactAutoFixHistoryList.ItemsSource = WpfAutoFixComposition.HistoryLines(progress.History);
            CompactAutoFixHistoryPanel.Visibility = progress.History.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private void CompactAutoFixCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_autoFixCompletion != null)
        {
            _autoFixCompletion.TrySetResult();
            return;
        }
        if (_autoFixCancellation == null || _autoFixCancellation.IsCancellationRequested) return;
        CompactAutoFixCancelButton.IsEnabled = false;
        CompactAutoFixCancelButton.Content = "Отменяем…";
        CompactAutoFixCurrentText.Text = "Отменяем…";
        _autoFixCancellation.Cancel();
    }

    private Task WaitForAutoFixAcknowledgementAsync()
    {
        _autoFixCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CompactAutoFixCancelButton.Content = "ОК";
        CompactAutoFixCancelButton.IsEnabled = true;
        CompactAutoFixCancelButton.Focus();
        return _autoFixCompletion.Task;
    }

    private async void FullWindow_Closed(object? sender, EventArgs e)
    {
        _fullWindow = null;
        if (System.Windows.Application.Current.MainWindow is StartupWindow)
        {
            _returningToStartup = true;
            Close();
            return;
        }

        System.Windows.Application.Current.MainWindow = this;
        Show();
        await StartVisibleWorkAsync();
    }

    private async Task RunOperationAsync(string status, Func<Task> operation)
    {
        if (_operationRunning)
            return;
        _operationRunning = true;
        AutoFixButton.IsEnabled = HelpButton.IsEnabled = OpenFullButton.IsEnabled = false;
        OperationText.Text = status;
        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, status, nameof(CompactMainWindow));
            OperationText.Text = "Не удалось выполнить операцию. Попробуйте позже.";
        }
        finally
        {
            _operationRunning = false;
            AutoFixButton.IsEnabled = HelpButton.IsEnabled = OpenFullButton.IsEnabled = true;
        }
    }

    private void CopyDeviceId_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_deviceId))
            return;
        Clipboard.SetText(_deviceId);
        OperationText.Text = "Device ID скопирован.";
    }

    private void LicenseSnapshotChanged(LicenseObservationSnapshot snapshot)
    {
        if (_returningToStartup || snapshot is null ||
            !string.Equals(snapshot.ClientId, _client.ClientId, StringComparison.Ordinal))
            return;
        if (snapshot.Decision == LicenseDecision.Allowed)
        {
            _license = snapshot;
            return;
        }

        Dispatcher.BeginInvoke(() => ReturnToStartupForDeniedLicense(snapshot));
    }

    private void ReturnToStartupForDeniedLicense(LicenseObservationSnapshot snapshot)
    {
        if (_returningToStartup)
            return;
        _returningToStartup = true;
        StopVisibleWork();
        var startup = new StartupWindow(string.IsNullOrWhiteSpace(snapshot.Message)
            ? "Лицензия больше не разрешает работу."
            : snapshot.Message);
        System.Windows.Application.Current.MainWindow = startup;
        startup.Show();
        Close();
    }

    private sealed class CompactAutoFixProgress : IProgressService
    {
        private readonly CompactMainWindow _window;
        public CompactAutoFixProgress(CompactMainWindow window) => _window = window;

        public void SetProgress(int percent, string stepName) => _window.Dispatcher.Invoke(() =>
        {
            _window.CompactAutoFixProgressBar.IsIndeterminate = false;
            _window.CompactAutoFixProgressBar.Value = Math.Clamp(percent, 0, 100);
            _window.CompactAutoFixCurrentText.Text = string.IsNullOrWhiteSpace(stepName)
                ? "Выполняем исправление…"
                : stepName;
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        StopVisibleWork();
        _lifetime.Cancel();
        _lifetime.Dispose();
        LicenseObservationSnapshotStore.Instance.SnapshotChanged -= LicenseSnapshotChanged;
        base.OnClosed(e);
    }
}
