using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using HonestFlow.Application.Bootstrap;
using HonestFlow.Application.Core;
using HonestFlow.Application.Diagnostics;
using HonestFlow.Application.Feedback;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Installation.Planning;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.Lm;
using HonestFlow.Application.PointStatus;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Infrastructure.DeviceIdentity;
using System.Windows.Media;
using System.Windows.Shapes;
using HonestFlow.Application.RemoteAccess;
using HonestFlow.Application.Ui;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Infrastructure.Licensing;
using HonestFlow.Models;

namespace HonestFlow.WpfPrototype;

public partial class MainWindow : Window
{
    private readonly StartupResult? _startup;
    private readonly IPData? _client;
    private LicenseObservationSnapshot? _license;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ApplicationStartupController _startupController = new();
    private readonly ILogService _logService = new LogService();
    private readonly ExternalApplicationLauncher _externalLauncher = new();
    private Section _section = Section.Home;
    private bool _operationRunning;
    private bool _returningToStartup;
    private readonly TopologyPresentationService _topologyPresentation = new();
    private PointStatusRefreshService? _pointStatusRefresh;
    private PointStatusResult? _lastPointStatus;
    private ComponentVersionStatusService? _componentVersionStatusService;
    private readonly DispatcherTimer _logTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string? _deviceId;
    private bool _ratingSent;
    private ComponentVersionStatus[] _componentStatuses = Array.Empty<ComponentVersionStatus>();
    private readonly WindowsServiceSnapshotProvider _serviceSnapshotProvider = new();

    public MainWindow()
    {
        InitializeComponent();
        MoveToolsPanelIntoActionPanel();
    }

    private void MoveToolsPanelIntoActionPanel()
    {
        if (ToolsPanel.Parent is not Grid source || ActionPanel.Child is not Grid destination) return;
        source.Children.Remove(ToolsPanel);
        Grid.SetRow(ToolsPanel, 3);
        destination.Children.Add(ToolsPanel);
    }

    public MainWindow(StartupResult startup, IPData client, LicenseObservationSnapshot? license) : this()
    {
        _startup = startup;
        _client = client;
        _license = license;
        ClientNameText.Text = string.IsNullOrWhiteSpace(client.Name) ? "Рабочая точка" : client.Name;
        ApplicationVersionText.Text = $"HonestFlow v{GetApplicationVersion()}";
        UpdateLicenseSummary();
        SectionOutput.Text = "Готово к работе.";
        var ruDesktop = new RuDesktopService(_logService);
        var statusService = new PointStatusService(
            startup.UseRemoteConfigMode,
            startup.Ips?.Count ?? startup.RemoteIps?.Count ?? 0,
            startup.Ips ?? startup.RemoteIps,
            ruDesktop);
        _componentVersionStatusService = new ComponentVersionStatusService(_logService);
        _pointStatusRefresh = new PointStatusRefreshService(
            statusService,
            _componentVersionStatusService,
            new PointStatusReportBuilder());
        _logTimer.Tick += (_, _) => UpdateLiveLog();
        LicenseObservationSnapshotStore.Instance.SnapshotChanged += LicenseSnapshotChanged;
        Loaded += async (_, _) =>
        {
            await LoadDeviceIdAsync();
            await RefreshTopologyAsync();
            _logTimer.Start();
            _ = RunPeriodicLicenseRefreshAsync(_lifetime.Token);
        };
    }

    private void HomeNav_Click(object sender, RoutedEventArgs e) => ShowHome();
    private async void DiagnosticsNav_Click(object sender, RoutedEventArgs e)
    {
        ShowSection(Section.Diagnostics, "Инструменты ручного ремонта", "Папки, службы и системные средства для обслуживания рабочей точки.", null, null, null, null);
        await RefreshServiceToolsAsync();
    }
    private async void ComponentsNav_Click(object sender, RoutedEventArgs e)
    {
        ShowSection(Section.Components, "Компоненты", "Установленные и требуемые версии компонентов рабочей точки.", "Обновить всё", null, "Восстановить базу ЛМ", null);
        PopulateComponentLoadingRows();
        await LoadComponentVersionsAsync();
    }
    private void LogNav_Click(object sender, RoutedEventArgs e)
    {
        ShowSection(Section.Log, "Журнал операций", "Полный журнал текущего сеанса обновляется автоматически.", null, null, null, null);
        UpdateLiveLog(force: true);
    }

    private void ShowHome()
    {
        _section = Section.Home;
        DashboardPanel.Visibility = Visibility.Visible;
        ActionPanel.Visibility = Visibility.Collapsed;
        UpdateNavSelection(HomeNavButton);
    }

    private void ShowSection(Section section, string title, string description, string? primary, string? secondary, string? tertiary, string? quaternary)
    {
        _section = section;
        DashboardPanel.Visibility = Visibility.Collapsed;
        ActionPanel.Visibility = Visibility.Visible;
        SectionTitle.Text = title;
        SectionDescription.Text = description;
        ConfigureAction(PrimaryActionButton, primary);
        ConfigureAction(SecondaryActionButton, secondary);
        ConfigureAction(TertiaryActionButton, tertiary);
        ConfigureAction(QuaternaryActionButton, quaternary);
        SectionOutput.Text = section == Section.Log ? string.Empty : "Выберите действие.";
        bool components = section == Section.Components;
        ComponentsPanel.Visibility = components ? Visibility.Visible : Visibility.Collapsed;
        bool tools = section == Section.Diagnostics;
        ToolsPanel.Visibility = tools ? Visibility.Visible : Visibility.Collapsed;
        SectionOutputBorder.Visibility = components || tools ? Visibility.Collapsed : Visibility.Visible;
        UpdateNavSelection(section == Section.Diagnostics ? DiagnosticsNavButton : section == Section.Components ? ComponentsNavButton : LogNavButton);
    }

    private static void ConfigureAction(Button button, string? caption)
    {
        button.Content = caption ?? string.Empty;
        button.Visibility = string.IsNullOrWhiteSpace(caption) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void PrimaryAction_Click(object sender, RoutedEventArgs e)
    {
        switch (_section)
        {
            case Section.Diagnostics: await RunDiagnosticsAsync(send: true); break;
            case Section.Components: await InstallAsync(reinstall: false); break;
        }
    }

    private async void SecondaryAction_Click(object sender, RoutedEventArgs e)
    {
        switch (_section)
        {
            case Section.Diagnostics: await RunDiagnosticsAsync(send: false); break;
            case Section.Components: await InstallAsync(reinstall: true); break;
        }
    }

    private async void TertiaryAction_Click(object sender, RoutedEventArgs e)
    {
        if (_section == Section.Components) await RestoreLmDatabaseAsync();
    }

    private async void QuaternaryAction_Click(object sender, RoutedEventArgs e)
    {
        await Task.CompletedTask;
    }

    private async Task RunDiagnosticsAsync(bool send)
    {
        DiagnosticLogSelection? selection = SelectDiagnosticLogs();
        if (selection == null) return;
        await RunOperationAsync("Собираем диагностику…", async () =>
        {
            var ruDesktop = new RuDesktopService(_logService);
            var workflow = new DiagnosticWorkflowService(
                new DiagnosticArchiveService(_logService),
                new DiagnosticsEmailSender(_logService, ruDesktop));
            DiagnosticArchiveInfo archive = await workflow.CreateArchiveAsync(
                selection, _license?.PointAddress, _lifetime.Token);
            SectionOutput.Text = $"Архив создан: {archive.ArchivePath}";
            if (send)
            {
                await workflow.SendArchiveAsync(archive, (percent, message) => Dispatcher.Invoke(() => SectionOutput.Text = $"{message} ({percent}%)"));
                SectionOutput.Text = "Диагностика отправлена в поддержку.";
            }
        });
    }

    private async Task InstallAsync(bool reinstall)
    {
        if (_startup == null || _client == null) return;
        await RunOperationAsync(reinstall ? "Переустанавливаем компоненты…" : "Выполняем установку…", async () =>
        {
            ComponentInstallationWorkflow workflow = CreateInstallationWorkflow();
            ComponentOperationReadiness readiness = workflow.CheckReadiness(_client, checkLmRequirements: !reinstall);
            if (!readiness.CanContinue) throw new InvalidOperationException(readiness.Status == ComponentOperationReadinessStatus.AdministratorRequired ? "Запустите HonestFlow от имени администратора." : "Не выбрана рабочая точка.");
            bool result = reinstall
                ? await workflow.ReinstallAsync(_client, Enum.GetValues<InstallationComponent>())
                : await workflow.InstallAsync(_client, _lifetime.Token);
            SectionOutput.Text = result ? "Операция завершена успешно." : "Операция завершена без подтверждения успеха. Проверьте журнал.";
        });
    }

    private void PopulateComponentLoadingRows()
    {
        ComponentVersionsGrid.ItemsSource = new[]
        {
            LoadingRow(InstallationComponent.AtolDriver, "Драйвер ККТ"),
            LoadingRow(InstallationComponent.LmModule, "ЛМ ЧЗ"),
            LoadingRow(InstallationComponent.Esm, "ЕСМ"),
            LoadingRow(InstallationComponent.Controller, "Контроллер ЛМ")
        };
    }

    private static ComponentVersionRow LoadingRow(InstallationComponent component, string name) => new()
    {
        Component = component, Name = name, InstalledVersion = "Проверяем…",
        ExpectedVersion = "Проверяем…", MatchText = "—", CanUpdate = false
    };

    private async Task LoadComponentVersionsAsync()
    {
        if (_componentVersionStatusService == null || _client == null) return;
        try
        {
            _componentStatuses = await Task.Run(
                () => _componentVersionStatusService.GetStatuses(_client, _startup?.RemoteVersions),
                _lifetime.Token);
            PopulateComponentVersions();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.LogException(ex, "WPF component version refresh failed", nameof(MainWindow));
            SectionDescription.Text = "Не удалось определить версии компонентов: " + ex.Message;
        }
    }

    private void PopulateComponentVersions()
    {
        InstallationComponent[] components = Enum.GetValues<InstallationComponent>();
        ComponentVersionsGrid.ItemsSource = _componentStatuses.Select((status, index) => new ComponentVersionRow
        {
            Component = index < components.Length ? components[index] : components[0],
            Name = status.ComponentName,
            InstalledVersion = string.IsNullOrWhiteSpace(status.InstalledVersion) ? "Не установлена" : status.InstalledVersion,
            ExpectedVersion = string.IsNullOrWhiteSpace(status.ExpectedVersion) ? "Не задана" : status.ExpectedVersion,
            MatchText = status.State switch
            {
                ComponentVersionState.Current => "Да",
                ComponentVersionState.UpdateRequired => "Нет",
                ComponentVersionState.NotInstalled => "Не установлен",
                _ => "Неизвестно"
            },
            CanUpdate = status.State != ComponentVersionState.Current
        }).OrderBy(row => row.Component switch
        {
            InstallationComponent.AtolDriver => 0,
            InstallationComponent.LmModule => 1,
            InstallationComponent.Esm => 2,
            InstallationComponent.Controller => 3,
            _ => 4
        }).ToArray();
    }

    private async void UpdateComponent_Click(object sender, RoutedEventArgs e)
    {
        if (_client == null || sender is not Button { Tag: InstallationComponent component }) return;
        await RunOperationAsync($"Переустанавливаем {ComponentDisplayName(component)}…", async () =>
        {
            ComponentInstallationWorkflow workflow = CreateInstallationWorkflow();
            ComponentOperationReadiness readiness = workflow.CheckReadiness(_client, checkLmRequirements: false);
            if (!readiness.CanContinue)
                throw new InvalidOperationException(readiness.Status == ComponentOperationReadinessStatus.AdministratorRequired
                    ? "Запустите HonestFlow от имени администратора."
                    : "Не выбрана рабочая точка.");
            await workflow.ReinstallAsync(_client, new[] { component });
        });
        await LoadComponentVersionsAsync();
    }

    private async Task ReinstallSelectedAsync()
    {
        if (_client == null) return;
        InstallationComponent[]? selected = SelectComponents();
        if (selected == null || selected.Length == 0) return;
        await RunOperationAsync("Переустанавливаем выбранные компоненты…", async () =>
        {
            ComponentInstallationWorkflow workflow = CreateInstallationWorkflow();
            ComponentOperationReadiness readiness = workflow.CheckReadiness(_client, checkLmRequirements: false);
            if (!readiness.CanContinue)
                throw new InvalidOperationException(readiness.Status == ComponentOperationReadinessStatus.AdministratorRequired
                    ? "Запустите HonestFlow от имени администратора."
                    : "Не выбрана рабочая точка.");
            bool result = await workflow.ReinstallAsync(_client, selected);
            SectionOutput.Text = result ? "Выбранные компоненты переустановлены." : "Переустановка не завершена. Проверьте журнал.";
        });
    }

    private InstallationComponent[]? SelectComponents()
    {
        CheckBox[] checks = Enum.GetValues<InstallationComponent>().Select(component => new CheckBox
        {
            Content = ComponentDisplayName(component), Tag = component, IsChecked = true, FontSize = 15,
            Margin = new Thickness(0, 7, 0, 7)
        }).ToArray();
        var items = new StackPanel { Margin = new Thickness(24, 18, 24, 12) };
        foreach (CheckBox check in checks) items.Children.Add(check);
        var ok = new Button { Content = "Переустановить выбранное", MinWidth = 210, Height = 42, IsDefault = true, Margin = new Thickness(8) };
        var cancel = new Button { Content = "Отмена", MinWidth = 100, Height = 42, IsCancel = true, Margin = new Thickness(8) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel); buttons.Children.Add(ok);
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons); root.Children.Add(items);
        var dialog = new Window { Owner = this, Title = "Переустановка компонентов", Width = 480, Height = 390,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Content = root };
        ok.Click += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() != true) return null;
        return checks.Where(check => check.IsChecked == true).Select(check => (InstallationComponent)check.Tag).ToArray();
    }

    private static string ComponentDisplayName(InstallationComponent component) => component switch
    {
        InstallationComponent.LmModule => "ЛМ ЧЗ",
        InstallationComponent.AtolDriver => "Драйвер ККТ АТОЛ",
        InstallationComponent.Esm => "ЕСМ",
        InstallationComponent.Controller => "Контроллер ЛМ",
        _ => component.ToString()
    };

    private DiagnosticLogSelection? SelectDiagnosticLogs()
    {
        var system = DiagnosticCheckBox("Сведения о системе и статусы служб");
        var honestFlow = DiagnosticCheckBox("Логи HonestFlow");
        var lm = DiagnosticCheckBox("Логи ЛМ ЧЗ");
        var esm = DiagnosticCheckBox("Логи ЕСМ");
        var kkt = DiagnosticCheckBox("Лог ККТ / АТОЛ");
        CheckBox[] checks = { system, honestFlow, lm, esm, kkt };

        var items = new StackPanel { Margin = new Thickness(24, 12, 24, 12) };
        items.Children.Add(new TextBlock
        {
            Text = "Выберите данные, которые нужно включить в диагностический архив:",
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        foreach (CheckBox check in checks) items.Children.Add(check);

        var collect = new Button { Content = "Продолжить", MinWidth = 140, Height = 42, IsDefault = true, Margin = new Thickness(8) };
        var cancel = new Button { Content = "Отмена", MinWidth = 100, Height = 42, IsCancel = true, Margin = new Thickness(8) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel); buttons.Children.Add(collect);
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons); root.Children.Add(items);
        var dialog = new Window
        {
            Owner = this, Title = "Сбор диагностики", Width = 520, Height = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Content = root
        };
        collect.Click += (_, _) =>
        {
            if (checks.Any(check => check.IsChecked == true)) dialog.DialogResult = true;
            else MessageBox.Show(dialog, "Выберите хотя бы один пункт.", "Сбор диагностики", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        if (dialog.ShowDialog() != true) return null;
        return new DiagnosticLogSelection
        {
            IncludeSystemInfo = system.IsChecked == true,
            IncludeHonestFlow = honestFlow.IsChecked == true,
            IncludeLm = lm.IsChecked == true,
            IncludeEsm = esm.IsChecked == true,
            IncludeKkt = kkt.IsChecked == true
        };
    }

    private static CheckBox DiagnosticCheckBox(string caption) => new()
    {
        Content = caption, IsChecked = true, FontSize = 15, Margin = new Thickness(0, 7, 0, 7)
    };

    private async Task RestoreLmDatabaseAsync()
    {
        if (_startup == null || _client == null) return;
        await RunOperationAsync("Восстанавливаем базу ЛМ ЧЗ…", async () =>
        {
            var progress = new MainWindowProgress(this);
            var dialogs = new WpfDialogs(this);
            ILicenseOperationGuard guard = CreateLicenseGuard();
            var service = new LmDatabaseRestoreService(_logService, progress, dialogs, guard, _startup.UseRemoteConfigMode);
            bool result = await service.Restore(_client);
            SectionOutput.Text = result ? "База ЛМ ЧЗ восстановлена." : "Восстановление не завершено.";
        });
    }

    private async Task RefreshLicenseAsync()
    {
        if (_startup == null || _client == null) return;
        await RunOperationAsync("Проверяем лицензию…", async () =>
        {
            var workflow = new LicenseRefreshWorkflow(_startup.AuthService, _logService);
            _license = await workflow.RefreshAsync(_client, null, _lifetime.Token);
            UpdateLicenseSummary();
            string message = string.IsNullOrWhiteSpace(_license?.Message)
                ? "Проверка лицензии завершена."
                : _license.Message;
            SectionOutput.Text = $"Решение: {_license?.Decision}" + Environment.NewLine + message;

            if (_license?.Decision == LicenseDecision.Allowed)
            {
                MessageBox.Show(this, message, "Лицензия обновлена",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ReturnToStartupForDeniedLicense(_license);
        });
    }

    private async Task RunPeriodicLicenseRefreshAsync(CancellationToken cancellationToken)
    {
        if (_startup == null)
            return;
        var workflow = new LicenseRefreshWorkflow(_startup.AuthService, _logService);
        await workflow.RunPeriodicAsync(() => _client, cancellationToken);
    }

    private void LicenseSnapshotChanged(LicenseObservationSnapshot snapshot)
    {
        if (_returningToStartup || snapshot == null ||
            !string.Equals(snapshot.ClientId, _client?.ClientId, StringComparison.Ordinal) ||
            snapshot.Decision == LicenseDecision.Allowed)
            return;

        Dispatcher.BeginInvoke(() => ReturnToStartupForDeniedLicense(snapshot));
    }

    private void ReturnToStartupForDeniedLicense(LicenseObservationSnapshot? snapshot)
    {
        if (_returningToStartup)
            return;

        _returningToStartup = true;
        string notice = snapshot?.Decision switch
        {
            LicenseDecision.ClientDisabled => "Лицензия клиента отключена. Обратитесь в поддержку.",
            LicenseDecision.DeviceDisabled => "Это устройство отключено в лицензии. Обратитесь в поддержку.",
            LicenseDecision.DeviceNotRegistered => "Устройство больше не зарегистрировано в лицензии.",
            LicenseDecision.ManifestExpired => "Срок действия лицензии истёк.",
            LicenseDecision.VersionTooOld => "Для этой лицензии требуется более новая версия HonestFlow.",
            _ => string.IsNullOrWhiteSpace(snapshot?.Message)
                ? "Лицензия больше не разрешает работу."
                : snapshot.Message
        };

        var startupWindow = new StartupWindow(notice);
        System.Windows.Application.Current.MainWindow = startupWindow;
        startupWindow.Show();
        Close();
    }
    private void UpdateLicenseSummary()
    {
        if (_license?.Decision != LicenseDecision.Allowed)
        {
            TariffText.Text = "Тариф: недоступен";
            return;
        }

        bool repair = _license.Features?.Contains(
            HonestFlow.Models.Licensing.LicenseFeature.ViewAndRepair) == true;
        bool maintenance = _license.Features?.Contains(
            HonestFlow.Models.Licensing.LicenseFeature.InstallAndMaintenance) == true;
        TariffText.Text = (repair, maintenance) switch
        {
            (true, true) => "Тариф: Полный",
            (true, false) => "Тариф: Диагностика и ремонт",
            (false, true) => "Тариф: Установка и обслуживание",
            _ => "Тариф: без доступных функций"
        };
    }
    private async Task SendHelpAsync()
    {
        if (_startup == null || _client == null || _license == null) return;
        await RunOperationAsync("Отправляем запрос помощи…", async () =>
        {
            var session = new ApplicationStartupSession(
                _startup,
                _logService,
                new HonestFlow.Application.Auth.SellerAuthenticationWorkflow(_startup.AuthService, LicenseObservationSnapshotStore.Instance));
            await _startupController.SendHelpRequestAsync(session, _license, _lifetime.Token);
            SectionOutput.Text = "Запрос помощи отправлен.";
        });
    }

    private async void RequestHelp_Click(object sender, RoutedEventArgs e) => await SendHelpAsync();
    private async void RefreshLicense_Click(object sender, RoutedEventArgs e) => await RefreshLicenseAsync();

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        if (_startup == null || _returningToStartup) return;
        _returningToStartup = true;
        try
        {
            var session = new ApplicationStartupSession(
                _startup,
                _logService,
                new HonestFlow.Application.Auth.SellerAuthenticationWorkflow(
                    _startup.AuthService,
                    LicenseObservationSnapshotStore.Instance));
            await _startupController.LogoutAsync(session, _lifetime.Token);
        }
        finally
        {
            var startupWindow = new StartupWindow("Сессия завершена. Войдите под нужной учётной записью.");
            System.Windows.Application.Current.MainWindow = startupWindow;
            startupWindow.Show();
            Close();
        }
    }

    private async void Rate_Click(object sender, RoutedEventArgs e)
    {
        if (_client == null) return;
        await RunOperationAsync("Отправляем оценку…", async () =>
        {
            await new AppRatingEmailSender(_logService).Send(_client.Name, _license?.PointAddress);
            _ratingSent = true;
            RateButton.Content = "♥  Спасибо за оценку";
            RateButton.IsEnabled = false;
            SectionOutput.Text = "Спасибо за оценку HonestFlow.";
        });
    }

    private ComponentInstallationWorkflow CreateInstallationWorkflow()
    {
        if (_startup == null) throw new InvalidOperationException("Стартовая сессия отсутствует.");
        var service = new InstallationService(_logService, new MainWindowProgress(this), new WpfDialogs(this), CreateLicenseGuard(), _startup.UseRemoteConfigMode);
        return new ComponentInstallationWorkflow(service);
    }

    private ILicenseOperationGuard CreateLicenseGuard() => new LicenseOperationGuard(new LicenseAccessPolicy(
        LicenseRuntimeConfiguration.FromEnvironment().EnforcementMode,
        LicenseObservationSnapshotStore.Instance,
        () => _client?.ClientId));

    private async Task RunOperationAsync(string status, Func<Task> operation)
    {
        if (_operationRunning) return;
        _operationRunning = true;
        SetActionsEnabled(false);
        SectionOutput.Text = status;
        try { await operation(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.LogException(ex, status, nameof(MainWindow));
            SectionOutput.Text = ex.Message;
        }
        finally { _operationRunning = false; SetActionsEnabled(true); }
    }

    private void SetActionsEnabled(bool enabled)
    {
        PrimaryActionButton.IsEnabled = enabled;
        SecondaryActionButton.IsEnabled = enabled;
        TertiaryActionButton.IsEnabled = enabled;
        QuaternaryActionButton.IsEnabled = enabled;
        FooterHelpButton.IsEnabled = enabled;
        RefreshLicenseButton.IsEnabled = enabled;
        SimpleFixButton.IsEnabled = enabled;
        RateButton.IsEnabled = enabled && !_ratingSent;
    }

    private void RunExternal(Action action, string title)
    {
        try { action(); SectionOutput.Text = $"Открыто: {title}."; }
        catch (Exception ex) { SectionOutput.Text = ex.Message; }
    }

    private async void RefreshTopology_Click(object sender, RoutedEventArgs e) => await RefreshTopologyAsync();

    private void SimpleView_Click(object sender, RoutedEventArgs e) => ShowSimpleView();
    private void DetailedView_Click(object sender, RoutedEventArgs e) => ShowDetailedView();
    private async void SimpleFix_Click(object sender, RoutedEventArgs e)
    {
        ComponentsNav_Click(ComponentsNavButton, new RoutedEventArgs());
        await InstallAsync(reinstall: false);
    }

    private void ShowSimpleView()
    {
        SimpleStatusPanel.Visibility = Visibility.Visible;
        DetailedTopologyView.Visibility = Visibility.Collapsed;
        DetailedProblemPanel.Visibility = Visibility.Collapsed;
        TopologyInfoIcon.Visibility = Visibility.Collapsed;
        StatusSectionTitle.Text = "Состояние рабочего места";
        SimpleViewButton.Background = BrushFrom("#0868D9");
        SimpleViewButton.Foreground = Brushes.White;
        DetailedViewButton.Background = Brushes.White;
        DetailedViewButton.Foreground = BrushFrom("#111827");
    }

    private void ShowDetailedView()
    {
        SimpleStatusPanel.Visibility = Visibility.Collapsed;
        DetailedTopologyView.Visibility = Visibility.Visible;
        DetailedProblemPanel.Visibility = Visibility.Visible;
        TopologyInfoIcon.Visibility = Visibility.Visible;
        StatusSectionTitle.Text = "Схема кассового узла";
        DetailedViewButton.Background = BrushFrom("#0868D9");
        DetailedViewButton.Foreground = Brushes.White;
        SimpleViewButton.Background = Brushes.White;
        SimpleViewButton.Foreground = BrushFrom("#111827");
    }

    private async Task RefreshTopologyAsync()
    {
        if (_pointStatusRefresh == null || _client == null || _operationRunning) return;
        RefreshTopologyButton.IsEnabled = false;
        try
        {
            SetTopologyChecking();
            PointStatusRefreshResult refresh = await _pointStatusRefresh.RefreshAsync(
                _client,
                _startup?.RemoteVersions,
                includeLicensedComponents: true,
                _lifetime.Token);
            _lastPointStatus = refresh.PointStatus;
            TopologyPresentation presentation = _topologyPresentation.Create(refresh.PointStatus);
            ApplyTopology(presentation);
            ApplySimpleStatus(presentation);
            string checkedAt = DateTime.Now.ToString("d MMMM yyyy, HH:mm:ss");
            LastCheckText.Text = checkedAt;
            SimpleLastCheckText.Text = $"Последняя проверка: {checkedAt}";
            SectionOutput.Text = "Проверка связей завершена.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.LogException(ex, "WPF topology refresh failed", nameof(MainWindow));
            SectionOutput.Text = ex.Message;
        }
        finally { RefreshTopologyButton.IsEnabled = true; }
    }

    private void SetTopologyChecking()
    {
        Brush neutral = BrushFrom("#94A3B8");
        SimpleStatusHalo.Fill = BrushFrom("#EEF2F7");
        SimpleStatusLamp.Fill = neutral;
        SimpleStatusSymbol.Text = "…";
        SimpleStatusTitle.Text = "Проверяем рабочее место…";
        SimpleStatusDescription.Text = "Пожалуйста, подождите. HonestFlow проверяет готовность кассы и маркировки.";
        SimpleFixButton.Visibility = Visibility.Collapsed;
        SimpleHelpButton.Visibility = Visibility.Collapsed;
        foreach (Border border in new[] { EsmNodeBorder, ControllerNodeBorder, LmNodeBorder, KktNodeBorder }) border.BorderBrush = neutral;
        foreach (Line line in new[] { CloudEsmLine, EsmControllerLine, ControllerLmLine, EsmKktLine }) line.Stroke = neutral;
        foreach (Border marker in new[] { CloudEsmMarker, EsmControllerMarker, ControllerLmMarker, EsmKktMarker }) marker.Visibility = Visibility.Collapsed;
        EsmNodeStatusText.Text = ControllerNodeStatusText.Text = LmNodeStatusText.Text = KktNodeStatusText.Text = "Проверка…";
        AccountingEsmLine.Stroke = BrushFrom("#94A3B8");
        AccountingEsmLine.ToolTip = "Проверка товароучётной системы пока не выполняется.";
    }

    private void ApplyTopology(TopologyPresentation presentation)
    {
        ApplyFrame(EsmNodeBorder, EsmNodeIcon, EsmNodeStatusText, _lastPointStatus?.Esm, presentation.EsmFrame);
        ApplyFrame(ControllerNodeBorder, ControllerNodeIcon, ControllerNodeStatusText, _lastPointStatus?.Controller, presentation.ControllerFrame);
        ApplyFrame(LmNodeBorder, LmNodeIcon, LmNodeStatusText, _lastPointStatus?.Lm, presentation.LmFrame);
        ApplyFrame(KktNodeBorder, KktNodeIcon, KktNodeStatusText, _lastPointStatus?.Kkt, presentation.KktFrame);
        ApplyLink(CloudEsmLine, CloudEsmMarker, presentation.CloudToEsm);
        ApplyLink(EsmControllerLine, EsmControllerMarker, presentation.EsmToController);
        ApplyLink(ControllerLmLine, ControllerLmMarker, presentation.ControllerToLm);
        ApplyLink(EsmKktLine, EsmKktMarker, presentation.EsmToKkt);
    }

    private void ApplySimpleStatus(TopologyPresentation presentation)
    {
        TopologyLinkPresentation[] links =
        {
            presentation.CloudToEsm, presentation.EsmToController,
            presentation.ControllerToLm, presentation.EsmToKkt
        };
        TopologyVisualState[] frames =
        {
            presentation.EsmFrame, presentation.ControllerFrame,
            presentation.LmFrame, presentation.KktFrame
        };
        bool red = frames.Any(state => state == TopologyVisualState.Missing) ||
                   links.Any(link => link.State == TopologyVisualState.Missing);
        bool yellow = !red && links.Any(link => link.State == TopologyVisualState.Uncertain);

        if (red)
        {
            SetSimpleStatus("#FDE8EC", "#D91532", "×", "Работа невозможна",
                "Один из необходимых компонентов не работает или отсутствует. HonestFlow может попробовать восстановить его автоматически.",
                showFix: true, showHelp: true);
        }
        else if (yellow)
        {
            SetSimpleStatus("#FFF6D8", "#E3A008", "!", "Требуется внимание",
                "Работа может быть затруднена: одна из программ отвечает нестабильно. Можно посмотреть подробности или запустить исправление.",
                showFix: true, showHelp: false);
        }
        else
        {
            SetSimpleStatus("#E8F8F1", "#0E9F6E", "✓", "Всё готово к работе",
                "Касса и маркировка работают нормально. Можно продолжать работу.",
                showFix: false, showHelp: false);
        }
    }

    private void SetSimpleStatus(string halo, string lamp, string symbol, string title, string description, bool showFix, bool showHelp)
    {
        SimpleStatusHalo.Fill = BrushFrom(halo);
        SimpleStatusLamp.Fill = BrushFrom(lamp);
        SimpleStatusSymbol.Text = symbol;
        SimpleStatusTitle.Text = title;
        SimpleStatusDescription.Text = description;
        SimpleFixButton.Visibility = showFix ? Visibility.Visible : Visibility.Collapsed;
        SimpleHelpButton.Visibility = showHelp ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void ApplyFrame(Border border, TextBlock icon, TextBlock text, NodeStatus? status, TopologyVisualState state)
    {
        border.BorderBrush = state == TopologyVisualState.Healthy ? BrushFrom("#0E9F6E") : BrushFrom("#D91532");
        border.BorderThickness = new Thickness(2.2);
        border.ToolTip = status?.Details;
        text.Text = status?.ShortText ?? "Компонент не найден";
        text.Foreground = border.BorderBrush;
        icon.Foreground = border.BorderBrush;
    }

    private static void ApplyLink(Line line, Border marker, TopologyLinkPresentation presentation)
    {
        Brush color = presentation.State switch
        {
            TopologyVisualState.Healthy => BrushFrom("#0E9F6E"),
            TopologyVisualState.Missing => BrushFrom("#D91532"),
            _ => BrushFrom("#F4B740")
        };
        line.Stroke = color;
        line.StrokeThickness = 3;
        line.StrokeDashArray = null;
        line.ToolTip = presentation.Explanation;
        if (presentation.State == TopologyVisualState.Healthy || presentation.State == TopologyVisualState.Ignored)
        {
            marker.Visibility = Visibility.Collapsed;
            return;
        }
        marker.Visibility = Visibility.Visible;
        bool missing = presentation.State == TopologyVisualState.Missing;
        marker.Background = missing ? Brushes.White : color;
        marker.BorderBrush = missing ? color : Brushes.Transparent;
        marker.BorderThickness = missing ? new Thickness(1) : new Thickness(0);
        marker.ToolTip = presentation.Explanation;
        if (marker.Child is TextBlock symbol)
        {
            symbol.Text = missing ? "×" : "?";
            symbol.Foreground = missing ? color : Brushes.White;
            symbol.FontSize = missing ? 25 : 17;
            symbol.FontWeight = FontWeights.Bold;
            symbol.Margin = missing ? new Thickness(0, -4, 0, 0) : new Thickness(0);
        }
    }

    private static Brush BrushFrom(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;

    private async Task RefreshServiceToolsAsync()
    {
        try
        {
            ServiceSnapshot[] snapshots = await _serviceSnapshotProvider.GetSnapshotsAsync(_lifetime.Token);
            string[] fixedNames = { "regime", "yenisei", "esm-orchestrator", "esm-lm-controller", "uem-agent", "uem-updater", "atol-grpc-service" };
            var rows = fixedNames.Select(name => ServiceToolRow.From(name, snapshots.FirstOrDefault(snapshot =>
                string.Equals(snapshot.ServiceName, name, StringComparison.OrdinalIgnoreCase)))).ToList();
            ServiceSnapshot? esmCm = snapshots.FirstOrDefault(snapshot => snapshot.ServiceName.StartsWith("esm-cm-", StringComparison.OrdinalIgnoreCase));
            rows.Insert(3, esmCm == null
                ? new ServiceToolRow { DisplayName = "ЕСМ CM", ServiceName = "esm-cm-*", Status = "Не найдена", CanControl = false }
                : ServiceToolRow.From(esmCm.ServiceName, esmCm));
            ServiceToolsGrid.ItemsSource = rows;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { ShowToolError(ex); }
    }

    private void ServiceToolsGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ToolsScrollViewer.ScrollToVerticalOffset(ToolsScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
    private async void RefreshServices_Click(object sender, RoutedEventArgs e) => await RefreshServiceToolsAsync();
    private async void StartService_Click(object sender, RoutedEventArgs e) => await ControlServiceFromButtonAsync(sender, ServiceAction.Start);
    private async void StopService_Click(object sender, RoutedEventArgs e) => await ControlServiceFromButtonAsync(sender, ServiceAction.Stop);
    private async void RestartService_Click(object sender, RoutedEventArgs e) => await ControlServiceFromButtonAsync(sender, ServiceAction.Restart);

    private async Task ControlServiceFromButtonAsync(object sender, ServiceAction action)
    {
        if (sender is not Button { Tag: string serviceName } || serviceName.EndsWith("*", StringComparison.Ordinal)) return;
        if (action == ServiceAction.Stop && MessageBox.Show(this,
                $"Остановить службу {serviceName}? Работа связанного компонента будет прервана.",
                "Остановка службы", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            CreateLicenseGuard().Demand(HonestFlow.Models.Licensing.LicenseOperation.ManageServices);
            await ControlServiceAsync(serviceName, action);
            await RefreshServiceToolsAsync();
        }
        catch (InvalidOperationException ex)
        {
            await HandleUnavailableServiceAsync(serviceName, ex);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1060)
        {
            await HandleUnavailableServiceAsync(serviceName, ex);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            Logger.LogException(ex, $"Service access denied: {serviceName}", nameof(MainWindow));
            MessageBox.Show(this,
                $"Недостаточно прав для управления службой «{serviceName}». Запустите HonestFlow от имени администратора.",
                "Управление службой", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) { ShowToolError(ex); }
    }

    private async Task HandleUnavailableServiceAsync(string serviceName, Exception ex)
    {
        Logger.LogException(ex, $"Service unavailable: {serviceName}", nameof(MainWindow));
        await RefreshServiceToolsAsync();
        MessageBox.Show(this,
            $"Служба «{serviceName}» не найдена или больше недоступна. Список служб обновлён.",
            "Управление службой", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static async Task ControlServiceAsync(string serviceName, ServiceAction action)
    {
        using var service = new ServiceController(serviceName);
        service.Refresh();
        if (action is ServiceAction.Stop or ServiceAction.Restart && service.Status != ServiceControllerStatus.Stopped)
        {
            service.Stop();
            await Task.Run(() => service.WaitForStatus(
                ServiceControllerStatus.Stopped,
                TimeSpan.FromSeconds(30)));
        }

        if (action is ServiceAction.Start or ServiceAction.Restart)
        {
            service.Refresh();
            if (service.Status != ServiceControllerStatus.Running)
            {
                service.Start();
                await Task.Run(() => service.WaitForStatus(
                    ServiceControllerStatus.Running,
                    TimeSpan.FromSeconds(30)));
            }
        }
    }

    private void OpenKktDataFolder_Click(object sender, RoutedEventArgs e) =>
        OpenFolder(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATOL", "drivers10"));

    private void OpenEsmFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(@"C:\Program Files\ESP\ESM");
    private void OpenInstallerCache_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.InstallerCacheFolder);
    private void OpenKktServiceFolder_Click(object sender, RoutedEventArgs e)
    {
        string installed = _componentStatuses.FirstOrDefault(status => status.ComponentName.Contains("ККТ", StringComparison.OrdinalIgnoreCase))?.InstalledVersion ?? string.Empty;
        bool x64 = installed.Contains("64-bit", StringComparison.OrdinalIgnoreCase);
        string preferred = x64
            ? @"C:\Program Files\ATOL\Drivers10\KKT\atol-grpc-service"
            : @"C:\Program Files (x86)\ATOL\Drivers10\KKT\atol-grpc-service";
        OpenFolder(preferred);
    }

    private void OpenFolder(string path)
    {
        try
        {
            CreateLicenseGuard().Demand(HonestFlow.Models.Licensing.LicenseOperation.OpenLocalTools);
            if (!Directory.Exists(path))
            {
                MessageBox.Show(this, $"Папка не найдена:\n{path}", "Инструменты", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowToolError(ex); }
    }

    private void OpenServices_Click(object sender, RoutedEventArgs e) => OpenSystemTool("services.msc", elevated: false);
    private void OpenTaskManager_Click(object sender, RoutedEventArgs e) => OpenSystemTool("taskmgr.exe", elevated: false);
    private void OpenSystemTool_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string tool }) OpenSystemTool(tool, elevated: false); }
    private void OpenAdminTool_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string tool }) OpenSystemTool(tool, elevated: true); }

    private void OpenSystemTool(string tool, bool elevated)
    {
        try
        {
            CreateLicenseGuard().Demand(HonestFlow.Models.Licensing.LicenseOperation.OpenLocalTools);
            Process.Start(new ProcessStartInfo(tool) { UseShellExecute = true, Verb = elevated ? "runas" : string.Empty });
        }
        catch (Exception ex) { ShowToolError(ex); }
    }

    private void RemoveEsmGuiDuplicates_Click(object sender, RoutedEventArgs e)
    {
        Process[] processes = Process.GetProcessesByName("esm-gui");
        try
        {
            if (processes.Length <= 1)
            {
                MessageBox.Show(this, processes.Length == 0 ? "ЕСМ GUI не запущен." : "Запущен один экземпляр ЕСМ GUI. Дублей нет.", "ЕСМ GUI", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show(this, $"Найдено экземпляров ЕСМ GUI: {processes.Length}. Закрыть лишние и оставить один?", "Дубли ЕСМ GUI", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            Process keep = processes.OrderBy(process => TryGetStartTime(process)).First();
            foreach (Process process in processes.Where(process => process.Id != keep.Id))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            MessageBox.Show(this, "Лишние экземпляры ЕСМ GUI закрыты.", "ЕСМ GUI", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowToolError(ex); }
        finally { foreach (Process process in processes) process.Dispose(); }
    }

    private static DateTime TryGetStartTime(Process process) { try { return process.StartTime; } catch { return DateTime.MaxValue; } }
    private async void ToolsCollectAndSend_Click(object sender, RoutedEventArgs e) => await RunDiagnosticsAsync(send: true);
    private async void ToolsCollect_Click(object sender, RoutedEventArgs e) => await RunDiagnosticsAsync(send: false);
    private void ShowToolError(Exception ex)
    {
        Logger.LogException(ex, "WPF manual repair tool failed", nameof(MainWindow));
        MessageBox.Show(this, ex.Message, "Инструменты", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string GetApplicationVersion() =>
        (Assembly.GetEntryAssembly() ?? typeof(MainWindow).Assembly).GetName().Version?.ToString() ?? "unknown";

    private async Task LoadDeviceIdAsync()
    {
        _deviceId = _license?.DeviceId;
        if (string.IsNullOrWhiteSpace(_deviceId))
        {
            var identity = await new FileDeviceIdentityService(new DpapiDeviceIdentityStateProtector())
                .GetOrCreateAsync(_lifetime.Token);
            _deviceId = identity.IsAvailable ? identity.DeviceId : null;
        }
        ClientDetailsText.Text = string.IsNullOrWhiteSpace(_deviceId) ? "Device ID: недоступен" : $"Device ID: {_deviceId}";
        CopyDeviceIdButton.IsEnabled = !string.IsNullOrWhiteSpace(_deviceId);
    }

    private void CopyDeviceId_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_deviceId)) return;
        Clipboard.SetText(_deviceId);
        CopyDeviceIdButton.ToolTip = "Device ID скопирован";
    }

    private void UpdateNavSelection(Button active)
    {
        foreach (Button button in new[] { HomeNavButton, DiagnosticsNavButton, ComponentsNavButton, LogNavButton })
        {
            button.Background = Brushes.Transparent;
            button.Foreground = BrushFrom("#18243B");
            button.FontWeight = FontWeights.Normal;
        }
        active.Background = BrushFrom("#EDF4FD");
        active.Foreground = BrushFrom("#075CB8");
        active.FontWeight = FontWeights.SemiBold;
    }

    private void UpdateLiveLog(bool force = false)
    {
        if (!force && _section != Section.Log) return;
        try
        {
            string path = Logger.GetLogPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            string text = File.ReadAllText(path);
            if (SectionOutput.Text == text) return;
            bool followTail = SectionOutput.VerticalOffset >= SectionOutput.ExtentHeight - SectionOutput.ViewportHeight - 24;
            SectionOutput.Text = text;
            if (followTail || force) SectionOutput.ScrollToEnd();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void EsmNode_Click(object sender, MouseButtonEventArgs e) =>
        RunExternal(_externalLauncher.OpenEsm, "ЕСМ");

    private void KktNode_Click(object sender, MouseButtonEventArgs e) =>
        RunExternal(_externalLauncher.OpenKktDriver, "Драйвер ККТ");

    private async void LmNode_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            using var api = new LmApiClient();
            ApiResponse<LmStatus> response = await api.GetStatus();
            string content = response.RawResponse;
            if (string.IsNullOrWhiteSpace(content))
                content = $"HTTP {(int)response.StatusCode} {response.StatusCode}\n{response.ErrorMessage}";
            ShowTextDialog("Полный ответ ЛМ ЧЗ — /api/v2/status", content);
        }
        catch (Exception ex) { ShowTextDialog("Ответ ЛМ ЧЗ", ex.ToString()); }
    }

    private void ShowTextDialog(string title, string content)
    {
        var text = new TextBox { Text = content, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 13,
            TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(16) };
        var dialog = new Window { Owner = this, Title = title, Width = 900, Height = 650,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = text };
        dialog.ShowDialog();
    }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) ToggleMaximize(); else DragMove(); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    protected override void OnClosed(EventArgs e) { LicenseObservationSnapshotStore.Instance.SnapshotChanged -= LicenseSnapshotChanged; _logTimer.Stop(); _lifetime.Cancel(); base.OnClosed(e); }

    private sealed class MainWindowProgress : IProgressService
    {
        private readonly MainWindow _window;
        public MainWindowProgress(MainWindow window) => _window = window;
        public void SetProgress(int percent, string stepName) => _window.Dispatcher.Invoke(() => _window.SectionOutput.Text = $"{stepName} ({Math.Clamp(percent, 0, 100)}%)");
    }

    private sealed class WpfDialogs : IUserDialogService
    {
        private readonly Window _owner;
        public WpfDialogs(Window owner) => _owner = owner;
        public void ShowInformation(string message, string title) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        public void ShowWarning(string message, string title) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        public void ShowError(string message, string title) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        public bool Confirm(string message, string title, UserDialogIcon icon = UserDialogIcon.Warning) => Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        private MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage icon) => MessageBox.Show(_owner, message, title, buttons, icon);
    }

    private sealed class ServiceToolRow
    {
        public string DisplayName { get; init; } = string.Empty;
        public string ServiceName { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public bool CanControl { get; init; }

        public static ServiceToolRow From(string name, ServiceSnapshot? snapshot) => new()
        {
            DisplayName = name.ToLowerInvariant() switch
            {
                "regime" => "ЛМ ЧЗ — Regime",
                "yenisei" => "ЛМ ЧЗ — Yenisei",
                "esm-orchestrator" => "ЕСМ — Orchestrator",
                "esm-lm-controller" => "Контроллер ЛМ",
                "uem-agent" => "ККТ — UEM Agent",
                "uem-updater" => "ККТ — UEM Updater",
                "atol-grpc-service" => "ККТ — ATOL gRPC",
                _ when name.StartsWith("esm-cm-", StringComparison.OrdinalIgnoreCase) => "ЕСМ — CM",
                _ => name
            },
            ServiceName = snapshot?.ServiceName ?? name,
            Status = snapshot?.State?.ToLowerInvariant() switch
            {
                "running" => "Запущена",
                "stopped" => "Остановлена",
                "startpending" => "Запускается",
                "stoppending" => "Останавливается",
                null => "Не найдена",
                _ => snapshot.State
            },
            CanControl = snapshot != null
        };
    }

    private enum ServiceAction { Start, Stop, Restart }

    private sealed class ComponentVersionRow
    {
        public InstallationComponent Component { get; init; }
        public string Name { get; init; } = string.Empty;
        public string InstalledVersion { get; init; } = string.Empty;
        public string ExpectedVersion { get; init; } = string.Empty;
        public string MatchText { get; init; } = string.Empty;
        public bool CanUpdate { get; init; }
    }

    private enum Section { Home, Diagnostics, Components, Log }
}
