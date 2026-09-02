using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
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
using HonestFlow.Application.ServiceConnection;
using HonestFlow.Application.Ui;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Infrastructure.Licensing;
using HonestFlow.Models;
using HonestFlow.Models.Licensing;

namespace HonestFlow.UI;

public partial class MainWindow : Window
{
    private StartupResult? _startup;
    private IPData? _client;
    private LicenseObservationSnapshot? _license;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _installationCancellation;
    private TaskCompletionSource? _installationCompletion;
    private ApplicationStartupController? _startupController;
    private readonly ILogService _logService;
    private readonly ExternalApplicationLauncher _externalLauncher = new();
    private ApplicationMode _applicationMode = ApplicationMode.Service;
    private readonly LocalApplicationContext? _localContext;
    private ServiceRuntimeContext? _serviceRuntime;
    private ServiceConnectionState _serviceConnectionState = ServiceConnectionState.Disconnected;
    private CancellationTokenSource? _serviceLifetime;
    private readonly SemaphoreSlim _serviceConnectionGate = new(1, 1);
    private CancellationTokenSource? _silentServiceResumeCancellation;
    private bool _silentServiceResumeStarted;
    private bool _manualServiceConnectionRunning;
    private bool _paidExecutionEnabled;
    private readonly LocalRuntimeContext? _localRuntime;
    private readonly DiagnosticArchiveService? _localDiagnosticArchive;
    private Section _section = Section.Home;
    private bool _operationRunning;
    private bool _returningToStartup;
    private readonly TopologyPresentationService _topologyPresentation = new();
    private readonly DiagnosticIssuePresentationMapper _diagnosticPresentation = new();
    private PointStatusRefreshService? _pointStatusRefresh;
    private AutoFixWorkflow? _autoFixWorkflow;
    private PointStatusResult? _lastPointStatus;
    private DiagnosticsSnapshot? _lastDiagnostics;
    private ComponentVersionStatusService? _componentVersionStatusService;
    private readonly DispatcherTimer _logTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string? _deviceId;
    private bool _ratingSent;
    private ComponentVersionStatus[] _componentStatuses = Array.Empty<ComponentVersionStatus>();
    private readonly WindowsServiceSnapshotProvider _serviceSnapshotProvider;
    private readonly bool _diagnosticsDebugEnabled = DiagnosticsDebugMode.IsEnabled(Environment.GetCommandLineArgs());

    public MainWindow()
    {
        _logService = new LogService();
        _serviceSnapshotProvider = new WindowsServiceSnapshotProvider();
        InitializeComponent();
        DeveloperDiagnosticsButton.Visibility = _diagnosticsDebugEnabled ? Visibility.Visible : Visibility.Collapsed;
        MoveToolsPanelIntoActionPanel();
    }

    public MainWindow(LocalApplicationContext context) : this()
    {
        ArgumentNullException.ThrowIfNull(context);
        _localContext = context;
        _applicationMode = context.Mode;
        _paidExecutionEnabled = false;
        _logService = context.LogService;
        _localRuntime = context.Runtime;
        _pointStatusRefresh = context.PointStatusRefresh;
        _componentVersionStatusService = context.ComponentVersionStatus;
        _localDiagnosticArchive = context.DiagnosticArchive;
        _serviceSnapshotProvider = context.ServiceSnapshotProvider;

        ApplyFreePresentation();
        _logTimer.Tick += (_, _) => UpdateLiveLog();
        Loaded += async (_, _) =>
        {
            await RefreshTopologyAsync();
            _logTimer.Start();
        };
    }

    private void MoveToolsPanelIntoActionPanel()
    {
        if (ToolsPanel.Parent is not Grid source || ActionPanel.Child is not Grid destination) return;
        source.Children.Remove(ToolsPanel);
        Grid.SetRow(ToolsPanel, 3);
        destination.Children.Add(ToolsPanel);
    }

    private void ApplyFreePresentation()
    {
        Title = "HonestFlow Free";
        ProductTitleText.Text = "HonestFlow Free";
        ApplicationVersionText.Text = $"HonestFlow Free v{GetApplicationVersion()}";
        ClientNameText.Text = "Этот компьютер";
        ClientDetailsText.Text = Environment.MachineName;
        CopyDeviceIdButton.Visibility = Visibility.Collapsed;
        LogoutButton.Visibility = Visibility.Collapsed;
        ConnectServiceButton.Visibility = Visibility.Visible;
        RateButton.Visibility = Visibility.Collapsed;
        TariffText.Visibility = Visibility.Collapsed;
        RefreshLicenseButton.Visibility = Visibility.Collapsed;
        FooterHelpButton.Visibility = Visibility.Collapsed;
        SimpleHelpButton.Visibility = Visibility.Collapsed;
        SimpleFixButton.Visibility = Visibility.Collapsed;
        DetailedFixButton.Visibility = Visibility.Collapsed;
        UpdateInstructionAvailability();
        SendDiagnosticsButton.Visibility = Visibility.Collapsed;
        ServiceControlColumn.Visibility = Visibility.Collapsed;
        RemoveEsmGuiDuplicatesButton.Visibility = Visibility.Collapsed;
        AdminCommandPromptButton.Visibility = Visibility.Collapsed;
        AdminPowerShellButton.Visibility = Visibility.Collapsed;
        ExpectedVersionColumn.Visibility = Visibility.Collapsed;
        VersionMatchColumn.Visibility = Visibility.Collapsed;
        CloudStatusPanel.Visibility = Visibility.Collapsed;
        RemoteAccessStatusPanel.Visibility = Visibility.Collapsed;
        FreeFooterPanel.Visibility = Visibility.Visible;
        SectionOutput.Text = "HonestFlow Free готов к локальной диагностике.";
    }

    private void ApplyServicePresentation()
    {
        Title = "HonestFlow Service";
        ProductTitleText.Text = "HonestFlow Service";
        ApplicationVersionText.Text = $"HonestFlow Service v{GetApplicationVersion()}";
        ClientNameText.Text = string.IsNullOrWhiteSpace(_client?.Name) ? "Рабочая точка" : _client.Name;
        ClientDetailsText.Text = string.IsNullOrWhiteSpace(_client?.Inn)
            ? Environment.MachineName
            : $"ИНН: {_client.Inn}";
        CopyDeviceIdButton.Visibility = Visibility.Visible;
        LogoutButton.Content = "Отключить Service";
        LogoutButton.Visibility = Visibility.Visible;
        ConnectServiceButton.Visibility = Visibility.Collapsed;
        RateButton.Visibility = Visibility.Collapsed;
        TariffText.Text = "HonestFlow Service подключён";
        TariffText.Visibility = Visibility.Visible;
        RefreshLicenseButton.Visibility = Visibility.Collapsed;
        FooterHelpButton.Visibility = Visibility.Collapsed;
        SimpleHelpButton.Visibility = Visibility.Collapsed;
        SimpleFixButton.Visibility = Visibility.Visible;
        DetailedFixButton.Visibility = Visibility.Visible;
        UpdateInstructionAvailability();
        SendDiagnosticsButton.Visibility = Visibility.Collapsed;
        ServiceControlColumn.Visibility = Visibility.Visible;
        RemoveEsmGuiDuplicatesButton.Visibility = Visibility.Collapsed;
        AdminCommandPromptButton.Visibility = Visibility.Collapsed;
        AdminPowerShellButton.Visibility = Visibility.Collapsed;
        ExpectedVersionColumn.Visibility = Visibility.Visible;
        VersionMatchColumn.Visibility = Visibility.Visible;
        CloudStatusPanel.Visibility = Visibility.Visible;
        RemoteAccessStatusPanel.Visibility = Visibility.Visible;
        FreeFooterPanel.Visibility = Visibility.Collapsed;
        SectionOutput.Text = "HonestFlow Service подключён. Доступны автоматическое исправление и обслуживание компонентов.";
        ShowServiceConnectionStatus(ServiceConnectionState.Active, null);
    }

    private void ShowServiceConnectionStatus(ServiceConnectionState state, string? message)
    {
        _serviceConnectionState = state;
        string presentation = ServiceEntitlementEvaluator.Message(state, message);
        SectionOutput.Text = presentation;
        HeaderStatusDescription.Text = presentation;
    }

    public MainWindow(StartupResult startup, IPData client, LicenseObservationSnapshot? license) : this()
    {
        _applicationMode = ApplicationMode.Service;
        _paidExecutionEnabled = true;
        _startupController = new ApplicationStartupController();
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
            new PointStatusReportBuilder(),
            _logService);
        _autoFixWorkflow = CreateAutoFixWorkflow();
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
        ShowSection(
            Section.Components,
            "Компоненты",
            _applicationMode == ApplicationMode.Free
                ? "Установленные версии локальных компонентов. Управление компонентами доступно в HonestFlow Service."
                : "Установленные и требуемые версии компонентов рабочей точки.",
            _paidExecutionEnabled ? "Обновить всё" : null,
            null,
            _paidExecutionEnabled ? "Восстановить базу ЛМ" : null,
            null);
        PopulateComponentLoadingRows();
        await LoadComponentVersionsAsync();
    }
    private void LogNav_Click(object sender, RoutedEventArgs e)
    {
        ShowSection(Section.Log, "Журнал операций", "Полный журнал текущего сеанса обновляется автоматически.", null, null, null, null);
        UpdateLiveLog(force: true);
    }

    private void DeveloperDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (!_diagnosticsDebugEnabled || _pointStatusRefresh == null) return;
        var session = new DeveloperDiagnosticsSession(_lastDiagnostics, RefreshDeveloperDiagnosticsAsync);
        var window = new DeveloperDiagnosticsWindow(session) { Owner = this };
        window.Show();
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
        if (!_paidExecutionEnabled && send)
            return;
        DiagnosticLogSelection? selection = SelectDiagnosticLogs();
        if (selection == null) return;
        await RunOperationAsync("Собираем диагностику…", async () =>
        {
            if (!_paidExecutionEnabled)
            {
                if (_localDiagnosticArchive == null)
                    throw new InvalidOperationException("Локальный сбор диагностики не инициализирован.");
                DiagnosticArchiveInfo localArchive = await Task.Run(
                    () => _localDiagnosticArchive.CreateArchiveInfo(selection, pointAddress: null),
                    _lifetime.Token);
                SectionOutput.Text = $"Архив создан: {localArchive.ArchivePath}";
                return;
            }

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
        if (!_paidExecutionEnabled || _startup == null || _client == null) return;
        if (!reinstall)
        {
            await InstallAllAsync();
            return;
        }

        await RunInstallationOperationAsync(
            "Переустановка компонентов",
            "Подготовка переустановки…",
            async cancellationToken =>
            {
                ComponentInstallationWorkflow workflow = CreateInstallationWorkflow(showInstallationProgress: true);
                ComponentOperationReadiness readiness = workflow.CheckReadiness(_client, checkLmRequirements: false);
                if (!readiness.CanContinue) throw new InvalidOperationException(readiness.Status == ComponentOperationReadinessStatus.AdministratorRequired ? "Запустите HonestFlow от имени администратора." : "Не выбрана рабочая точка.");
                return await workflow.ReinstallAsync(
                    _client,
                    Enum.GetValues<InstallationComponent>(),
                    cancellationToken);
            },
            "Компоненты переустановлены.",
            "Переустановка не завершена. Проверьте журнал.",
            "Переустановка отменена.");
    }

    private async Task InstallAllAsync()
    {
        if (!_paidExecutionEnabled || _startup == null || _client == null) return;
        ComponentInstallationCompletionResult? completion = null;
        await RunInstallationOperationAsync(
            "Обновление компонентов",
            "Подготовка обновления…",
            async cancellationToken =>
            {
                ComponentInstallationWorkflow workflow = CreateInstallationWorkflow(showInstallationProgress: true);
                ComponentOperationReadiness readiness = workflow.CheckReadiness(_client, checkLmRequirements: true);
                if (!readiness.CanContinue)
                    throw new InvalidOperationException(readiness.Status == ComponentOperationReadinessStatus.AdministratorRequired
                        ? "Запустите HonestFlow от имени администратора."
                        : "Не выбрана рабочая точка.");
                InstallationOptions? options = ResolveInstallationOptions(readiness.SystemRequirements);
                if (options == null)
                    return false;
                completion = await workflow.InstallAndRegisterTsPiotAsync(_client, cancellationToken, options);
                return completion.IsSuccessful;
            },
            "Обновление компонентов завершено.",
            "Обновление завершено без подтверждения успеха. Проверьте журнал.",
            "Обновление компонентов отменено.",
            () => FormatInstallationCompletionStatus(completion));
    }

    private async Task RunInstallationOperationAsync(
        string title,
        string initialStatus,
        Func<CancellationToken, Task<bool>> operation,
        string successStatus,
        string failureStatus,
        string cancelledStatus,
        Func<string>? successStatusFactory = null)
    {
        if (!_paidExecutionEnabled) return;
        if (_operationRunning) return;

        _operationRunning = true;
        CancellationTokenSource installationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _installationCancellation = installationCancellation;
        ShowInstallationProgress(title, initialStatus);
        string finalStatus;
        try
        {
            bool result = await operation(installationCancellation.Token);
            finalStatus = successStatusFactory?.Invoke() ?? (result ? successStatus : failureStatus);
            if (result) InstallationProgressBar.Value = 100;
        }
        catch (OperationCanceledException) when (installationCancellation.IsCancellationRequested)
        {
            finalStatus = cancelledStatus;
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, title, nameof(MainWindow));
            finalStatus = ex.Message;
        }

        try
        {
            SectionOutput.Text = finalStatus;
            InstallationProgressText.Text = finalStatus;
            await RefreshAfterComponentOperationAsync();
            if (!_lifetime.IsCancellationRequested)
                await WaitForInstallationAcknowledgementAsync();
        }
        finally
        {
            _installationCompletion = null;
            HideInstallationProgress();
            installationCancellation.Dispose();
            if (ReferenceEquals(_installationCancellation, installationCancellation))
                _installationCancellation = null;
            _operationRunning = false;
        }
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

    private static string FormatInstallationCompletionStatus(ComponentInstallationCompletionResult? completion)
    {
        if (completion?.KktBootstrap != null)
            return completion.KktBootstrap.Message;
        return completion?.TsPiotRegistration?.Status switch
        {
            TsPiotRegistrationStatus.Success => "Компоненты установлены. ТС ПИоТ автоматически зарегистрирован.",
            TsPiotRegistrationStatus.EsmUnavailable => "Компоненты установлены. Автоматическая регистрация ТС ПИоТ не выполнена: ЕСМ недоступен.",
            TsPiotRegistrationStatus.KktNotDetected => "Компоненты установлены. Автоматическая регистрация ТС ПИоТ не выполнена: ККТ не обнаружена.",
            TsPiotRegistrationStatus.RegistrationFailed => "Компоненты установлены. Автоматическая регистрация ТС ПИоТ не выполнена.",
            _ => "Компоненты установлены."
        };
    }

    private async Task RefreshAfterComponentOperationAsync()
    {
        await LoadComponentVersionsAsync();
        await RefreshTopologyAsync(allowDuringOperation: true);
    }

    private void ShowInstallationProgress(string title, string initialStatus)
    {
        InstallationProgressTitle.Text = title;
        InstallationProgressBar.Value = 0;
        InstallationProgressBar.IsIndeterminate = false;
        InstallationProgressText.Text = initialStatus;
        AutoFixHistoryList.ItemsSource = null;
        AutoFixHistoryPanel.Visibility = Visibility.Collapsed;
        InstallationCancelButton.Content = "Отменить";
        InstallationCancelButton.IsEnabled = true;
        MainWindowInteractiveContent.IsEnabled = false;
        InstallationBusyOverlay.Visibility = Visibility.Visible;
        InstallationCancelButton.Focus();
    }

    private void HideInstallationProgress()
    {
        InstallationBusyOverlay.Visibility = Visibility.Collapsed;
        MainWindowInteractiveContent.IsEnabled = true;
        InstallationProgressTitle.Text = "Обновление компонентов";
        InstallationProgressBar.Value = 0;
        InstallationProgressBar.IsIndeterminate = false;
        InstallationProgressText.Text = "Подготовка…";
        AutoFixHistoryList.ItemsSource = null;
        AutoFixHistoryPanel.Visibility = Visibility.Collapsed;
        InstallationCancelButton.Content = "Отменить";
        InstallationCancelButton.IsEnabled = true;
    }

    private void InstallationCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_installationCompletion != null)
        {
            _installationCompletion.TrySetResult();
            return;
        }

        if (_installationCancellation == null || _installationCancellation.IsCancellationRequested) return;
        InstallationCancelButton.IsEnabled = false;
        InstallationCancelButton.Content = "Отменяем…";
        InstallationProgressText.Text = "Отменяем…";
        _installationCancellation.Cancel();
    }

    private Task WaitForInstallationAcknowledgementAsync()
    {
        _installationCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        InstallationCancelButton.Content = "ОК";
        InstallationCancelButton.IsEnabled = true;
        InstallationCancelButton.Focus();
        return _installationCompletion.Task;
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

    private ComponentVersionRow LoadingRow(InstallationComponent component, string name) => new()
    {
        Component = component, Name = name, InstalledVersion = "Проверяем…",
        ExpectedVersion = "Проверяем…", MatchText = "—", CanUpdate = false,
        ActionText = _paidExecutionEnabled ? "Переустановить" : "Недоступно",
        ActionTooltip = !_paidExecutionEnabled
            ? "Управление компонентами доступно в HonestFlow Service"
            : "Переустановить компонент"
    };

    private async Task LoadComponentVersionsAsync()
    {
        if (_componentVersionStatusService == null) return;
        try
        {
            _componentStatuses = await Task.Run(
                () => _applicationMode == ApplicationMode.Free
                    ? _componentVersionStatusService.GetLocalStatuses(_localRuntime ?? LocalRuntimeContext.CreateCurrent())
                    : _client == null
                        ? Array.Empty<ComponentVersionStatus>()
                        : _componentVersionStatusService.GetClientStatuses(_client, _startup?.RemoteVersions),
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
                ComponentVersionState.BelowMinimum => "Не поддерживается",
                ComponentVersionState.NotInstalled => "Не установлен",
                _ => "Неизвестно"
            },
            CanUpdate = _paidExecutionEnabled && status.State != ComponentVersionState.Current,
            ActionText = _paidExecutionEnabled ? "Переустановить" : "Недоступно",
            ActionTooltip = !_paidExecutionEnabled
                ? "Управление компонентами доступно в HonestFlow Service"
                : "Переустановить компонент"
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
        if (!_paidExecutionEnabled || _client == null || sender is not Button { Tag: InstallationComponent component }) return;
        string componentName = ComponentDisplayName(component);
        await RunInstallationOperationAsync(
            $"Переустановка: {componentName}",
            $"Подготовка переустановки {componentName}…",
            async cancellationToken =>
            {
                ComponentInstallationWorkflow workflow = CreateInstallationWorkflow(showInstallationProgress: true);
                ComponentOperationReadiness readiness = workflow.CheckReadiness(_client, checkLmRequirements: false);
                if (!readiness.CanContinue)
                    throw new InvalidOperationException(readiness.Status == ComponentOperationReadinessStatus.AdministratorRequired
                        ? "Запустите HonestFlow от имени администратора."
                        : "Не выбрана рабочая точка.");
                return await workflow.ReinstallAsync(_client, new[] { component }, cancellationToken);
            },
            $"{componentName} переустановлен.",
            $"Переустановка {componentName} не завершена. Проверьте журнал.",
            $"Переустановка {componentName} отменена.");
    }

    private async Task ReinstallSelectedAsync()
    {
        if (!_paidExecutionEnabled || _client == null) return;
        InstallationComponent[]? selected = SelectComponents();
        if (selected == null || selected.Length == 0) return;
        await RunInstallationOperationAsync(
            "Переустановка компонентов",
            "Подготовка выбранных компонентов…",
            async cancellationToken =>
            {
                ComponentInstallationWorkflow workflow = CreateInstallationWorkflow(showInstallationProgress: true);
                ComponentOperationReadiness readiness = workflow.CheckReadiness(_client, checkLmRequirements: false);
                if (!readiness.CanContinue)
                    throw new InvalidOperationException(readiness.Status == ComponentOperationReadinessStatus.AdministratorRequired
                        ? "Запустите HonestFlow от имени администратора."
                        : "Не выбрана рабочая точка.");
                return await workflow.ReinstallAsync(_client, selected, cancellationToken);
            },
            "Выбранные компоненты переустановлены.",
            "Переустановка не завершена. Проверьте журнал.",
            "Переустановка отменена.");
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
        if (!_paidExecutionEnabled || _startup == null || _client == null) return;
        await RunInstallationOperationAsync(
            "Восстановление ЛМ ЧЗ",
            "Проверка резервной базы…",
            async cancellationToken =>
            {
                var progress = new MainWindowProgress(this, showInstallationProgress: true);
                var dialogs = new WpfDialogs(this, suppressInformationMessages: true);
                ILicenseOperationGuard guard = CreateLicenseGuard();
                var service = new LmDatabaseRestoreService(
                    _logService,
                    progress,
                    dialogs,
                    guard,
                    useRemoteConfigMode: false,
                    packageSource: ApiConfigurationInstallationPackageSource.CreateRequired(_startup.AuthService));
                return await service.Restore(_client, cancellationToken);
            },
            "База ЛМ ЧЗ восстановлена.",
            "Восстановление ЛМ ЧЗ не завершено. Проверьте журнал.",
            "Восстановление ЛМ ЧЗ отменено.");
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
            !string.Equals(snapshot.ClientId, _client?.ClientId, StringComparison.Ordinal))
            return;

        if (_serviceRuntime is not null)
        {
            ServiceConnectionState state = ServiceEntitlementEvaluator.Evaluate(snapshot);
            if (state == ServiceConnectionState.Active &&
                string.Equals(snapshot.DeviceId, _serviceRuntime.DeviceId, StringComparison.Ordinal))
            {
                _license = snapshot;
                _serviceRuntime.Entitlement = snapshot;
                return;
            }

            if (state == ServiceConnectionState.Active)
                state = ServiceConnectionState.Unavailable;

            Dispatcher.BeginInvoke(async () => await DeactivateServiceAsync(state, snapshot.Message, logout: false));
            return;
        }

        if (snapshot.Decision == LicenseDecision.Allowed)
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
            LicenseDecision.LicenseNotIssued => "Устройство зарегистрировано. Лицензия для этого компьютера ещё не выдана.",
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
        if (!_paidExecutionEnabled || _startup == null || _client == null || _license == null) return;
        await RunOperationAsync("Отправляем запрос помощи…", async () =>
        {
            var session = new ApplicationStartupSession(
                _startup,
                _logService,
                new HonestFlow.Application.Auth.SellerAuthenticationWorkflow(_startup.AuthService, LicenseObservationSnapshotStore.Instance));
            await _startupController!.SendHelpRequestAsync(session, _license, _lifetime.Token);
            SectionOutput.Text = "Запрос помощи отправлен.";
        });
    }

    private async void RequestHelp_Click(object sender, RoutedEventArgs e) => await SendHelpAsync();
    private async void RefreshLicense_Click(object sender, RoutedEventArgs e) => await RefreshLicenseAsync();

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        if (_serviceRuntime is not null)
        {
            await DeactivateServiceAsync(
                ServiceConnectionState.Disconnected,
                "HonestFlow Service отключён.",
                logout: true);
            return;
        }

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
            await _startupController!.LogoutAsync(session, _lifetime.Token);
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

    private ComponentInstallationWorkflow CreateInstallationWorkflow(bool showInstallationProgress = false)
    {
        if (_startup == null) throw new InvalidOperationException("Стартовая сессия отсутствует.");
        var progress = new MainWindowProgress(this, showInstallationProgress);
        var packageSource = ApiConfigurationInstallationPackageSource.CreateRequired(_startup.AuthService);
        var service = new InstallationService(
            _logService,
            progress,
            new WpfDialogs(this, suppressInformationMessages: showInstallationProgress),
            CreateLicenseGuard(),
            useRemoteConfigMode: false,
            packageSource: packageSource);
        var registration = new TsPiotRegistrationWorkflow(
            new EsmTsPiotRegistrationClient(),
            new EsmApiPortProbe(),
            _logService,
            CreateLicenseGuard());
        KktBootstrapWorkflow? bootstrap = _pointStatusRefresh == null || _client == null
            ? null
            : WpfKktBootstrapComposition.Create(
                this,
                _startup,
                _client,
                registration,
                _pointStatusRefresh,
                progress,
                CreateLicenseGuard,
                ApplyPointStatusRefresh);
        return new ComponentInstallationWorkflow(
            service,
            Utils.IsAdministrator,
            LmSystemRequirements.Check,
            registration,
            bootstrap);
    }

    private ILicenseOperationGuard CreateLicenseGuard() =>
        new ServiceOperationGuard(() => _serviceRuntime);

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
        bool canAutoFix = _paidExecutionEnabled &&
            enabled && _lastDiagnostics?.Issues.Any(issue => issue.SuggestedFix.HasValue) == true;
        SimpleFixButton.IsEnabled = canAutoFix;
        DetailedFixButton.IsEnabled = canAutoFix;
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
    private void DetailedManualFix_Click(object sender, RoutedEventArgs e)
    {
        if (_lastDiagnostics?.Issues is not { Count: > 0 } issues)
            return;

        new InstructionWindow(issues) { Owner = this }.ShowDialog();
    }

    private async void SimpleFix_Click(object sender, RoutedEventArgs e)
    {
        await StartAutoFixAsync();
    }

    public async Task StartAutoFixAsync()
    {
        if (!_paidExecutionEnabled) return;
        if (_operationRunning || _autoFixWorkflow == null) return;

        _operationRunning = true;
        CancellationTokenSource autoFixCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _installationCancellation = autoFixCancellation;
        SetActionsEnabled(false);
        RefreshTopologyButton.IsEnabled = false;
        ShowInstallationProgress("Автоматическое исправление", "Проверяем состояние…");
        InstallationProgressBar.IsIndeterminate = true;
        try
        {
            AutoFixResult result = await _autoFixWorkflow.RunAsync(SetAutoFixProgress, autoFixCancellation.Token);
            while (result.Status == AutoFixStatus.RequiresConfirmation && result.Continuation != null)
            {
                bool confirmed = MessageBox.Show(this,
                    $"ЛМ ЧЗ настроен для другой организации.\n\nВы точно работаете под «{_client?.Name ?? "текущий клиент"}» на этом рабочем месте?\n\n" +
                    "При подтверждении будет переустановлен только ЛМ ЧЗ.",
                    "Подтверждение организации ЛМ ЧЗ",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) == MessageBoxResult.Yes;
                result = await _autoFixWorkflow.ContinueAsync(
                    result.Continuation, confirmed, SetAutoFixProgress, autoFixCancellation.Token);
            }

            if (result.State?.Refresh != null)
                ApplyPointStatusRefresh(result.State.Refresh);
            string finalMessage = WpfAutoFixComposition.ResultMessage(result);
            SectionOutput.Text = finalMessage;
            InstallationProgressText.Text = finalMessage;
            InstallationProgressBar.IsIndeterminate = false;
            InstallationProgressBar.Value = result.Status is AutoFixStatus.Success or AutoFixStatus.NoFixNeeded ? 100 : 0;
            if (!_lifetime.IsCancellationRequested)
                await WaitForInstallationAcknowledgementAsync();
        }
        catch (OperationCanceledException) when (autoFixCancellation.IsCancellationRequested)
        {
            SectionOutput.Text = "Автоматическое исправление отменено.";
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "WPF AutoFix failed", nameof(MainWindow));
            SectionOutput.Text = "Не удалось выполнить автоматическое исправление: " + ex.Message;
        }
        finally
        {
            _installationCompletion = null;
            HideInstallationProgress();
            autoFixCancellation.Dispose();
            if (ReferenceEquals(_installationCancellation, autoFixCancellation))
                _installationCancellation = null;
            _operationRunning = false;
            RefreshTopologyButton.IsEnabled = true;
            SetActionsEnabled(true);
        }
    }

    private AutoFixWorkflow CreateAutoFixWorkflow()
    {
        if (_startup == null || _client == null || _pointStatusRefresh == null)
            throw new InvalidOperationException("AutoFix нельзя создать до завершения запуска приложения.");
        return WpfAutoFixComposition.Create(
            this,
            _startup,
            _client,
            _logService,
            _pointStatusRefresh,
            new MainWindowProgress(this, showInstallationProgress: true),
            ResolveInstallationOptions,
            CreateLicenseGuard,
            ApplyPointStatusRefresh);
    }

    private void SetAutoFixProgress(AutoFixProgress progress)
    {
        Dispatcher.Invoke(() =>
        {
            SectionOutput.Text = progress.CurrentStep;
            InstallationProgressText.Text = progress.CurrentStep;
            AutoFixHistoryList.ItemsSource = WpfAutoFixComposition.HistoryLines(progress.History);
            AutoFixHistoryPanel.Visibility = progress.History.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        });
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

    private async Task RefreshTopologyAsync(bool allowDuringOperation = false)
    {
        if (_pointStatusRefresh == null || (_operationRunning && !allowDuringOperation)) return;
        RefreshTopologyButton.IsEnabled = false;
        try
        {
            SetTopologyChecking();
            PointStatusRefreshResult refresh = _applicationMode == ApplicationMode.Free
                ? await _pointStatusRefresh.RefreshLocalAsync(
                    _localRuntime ?? LocalRuntimeContext.CreateCurrent(),
                    _lifetime.Token)
                : await _pointStatusRefresh.RefreshForClientAsync(
                    _client ?? throw new InvalidOperationException("Service client is not initialized."),
                    _startup?.RemoteVersions,
                    _lifetime.Token);
            ApplyPointStatusRefresh(refresh);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.LogException(ex, "WPF topology refresh failed", nameof(MainWindow));
            SectionOutput.Text = ex.Message;
        }
        finally { RefreshTopologyButton.IsEnabled = true; }
    }

    private async Task<DiagnosticsSnapshot> RefreshDeveloperDiagnosticsAsync(CancellationToken cancellationToken)
    {
        if (_pointStatusRefresh == null)
            throw new InvalidOperationException("Diagnostics refresh is not initialized.");
        PointStatusRefreshResult refresh = _applicationMode == ApplicationMode.Free
            ? await _pointStatusRefresh.RefreshLocalAsync(
                _localRuntime ?? LocalRuntimeContext.CreateCurrent(),
                cancellationToken)
            : await _pointStatusRefresh.RefreshForClientAsync(
                _client ?? throw new InvalidOperationException("Service client is not initialized."),
                _startup?.RemoteVersions,
                cancellationToken);
        ApplyPointStatusRefresh(refresh);
        return refresh.Diagnostics;
    }

    private void ApplyPointStatusRefresh(PointStatusRefreshResult refresh)
    {
        _lastPointStatus = refresh.PointStatus;
        _lastDiagnostics = refresh.Diagnostics;
        UpdateInstructionAvailability();
        TopologyPresentation presentation = _topologyPresentation.Create(refresh.Diagnostics);
        ApplyTopology(presentation);
        ApplySimpleStatus(presentation);
        ApplyDetailedProblems(_diagnosticPresentation.Create(refresh.Diagnostics));
        string checkedAt = DateTime.Now.ToString("d MMMM yyyy, HH:mm:ss");
        LastCheckText.Text = checkedAt;
        SimpleLastCheckText.Text = $"Последняя проверка: {checkedAt}";
        SectionOutput.Text = "Проверка связей завершена.";
        bool canAutoFix = _paidExecutionEnabled &&
            !_operationRunning && refresh.Diagnostics.Issues.Any(issue => issue.SuggestedFix.HasValue);
        SimpleFixButton.IsEnabled = canAutoFix;
        DetailedFixButton.IsEnabled = canAutoFix;
        UpdateHeaderWorkState(refresh.Diagnostics);
    }

    private void UpdateInstructionAvailability() =>
        DetailedManualFixButton.Visibility = _lastDiagnostics?.Issues is { Count: > 0 }
            ? Visibility.Visible
            : Visibility.Collapsed;

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
        foreach (Border border in new[] { GismtNodeBorder, EsmNodeBorder, ControllerNodeBorder, LmNodeBorder, KktNodeBorder }) border.BorderBrush = neutral;
        foreach (Ellipse dot in new[] { GismtNodeStatusDot, EsmNodeStatusDot, ControllerNodeStatusDot, LmNodeStatusDot, KktNodeStatusDot }) dot.Fill = neutral;
        foreach (Border icon in new[] { GismtNodeIcon, EsmNodeIcon, ControllerNodeIcon, LmNodeIcon, KktNodeIcon }) SetTopologyIconBrush(icon, neutral);
        foreach (TextBlock text in new[] { GismtNodeStatusText, EsmNodeStatusText, ControllerNodeStatusText, LmNodeStatusText, KktNodeStatusText }) text.Foreground = neutral;
        foreach (Line line in new[] { CloudEsmLine, EsmControllerLine, ControllerLmLine, EsmKktLine }) line.Stroke = neutral;
        foreach (Border marker in new[] { CloudEsmMarker, EsmControllerMarker, ControllerLmMarker, EsmKktMarker }) marker.Visibility = Visibility.Collapsed;
        EsmNodeStatusText.Text = ControllerNodeStatusText.Text = LmNodeStatusText.Text = KktNodeStatusText.Text = "Проверка…";
        GismtNodeStatusText.Text = "Проверка…";
    }

    private void ApplyTopology(TopologyPresentation presentation)
    {
        if (_lastDiagnostics == null) return;

        ApplyStandaloneFrame(
            GismtNodeBorder, GismtNodeIcon, GismtNodeStatusDot, GismtNodeStatusText, _lastDiagnostics.Gismt,
            _diagnosticPresentation.GisMtStatus(_lastDiagnostics.Gismt), "ГИС МТ");
        ApplyFrame(
            EsmNodeBorder, EsmNodeIcon, EsmNodeStatusDot, EsmNodeStatusText, _lastDiagnostics.Esm, presentation.EsmFrame,
            _lastPointStatus?.Esm?.StatusText ?? _diagnosticPresentation.ComponentStatus(_lastDiagnostics.Esm, "ЕСМ недоступен"), "ТС ПИоТ");
        ApplyFrame(
            ControllerNodeBorder, ControllerNodeIcon, ControllerNodeStatusDot, ControllerNodeStatusText, _lastDiagnostics.Controller, presentation.ControllerFrame,
            _lastPointStatus?.Controller?.StatusText ?? _diagnosticPresentation.ComponentStatus(_lastDiagnostics.Controller, "Контроллер недоступен"), "Локальный контроллер");
        ApplyFrame(
            LmNodeBorder, LmNodeIcon, LmNodeStatusDot, LmNodeStatusText, _lastDiagnostics.Lm, presentation.LmFrame,
            _diagnosticPresentation.ComponentStatus(_lastDiagnostics.Lm, "Недоступен"), "ЛМ ЧЗ");
        ApplyFrame(
            KktNodeBorder, KktNodeIcon, KktNodeStatusDot, KktNodeStatusText, _lastDiagnostics.Kkt, presentation.KktFrame,
            _lastPointStatus?.Kkt?.StatusText ?? _diagnosticPresentation.ComponentStatus(_lastDiagnostics.Kkt, "Служба остановлена"), "ККТ");
        ApplyLink(CloudEsmLine, CloudEsmMarker, presentation.CloudToEsm,
            _diagnosticPresentation.ConnectionDetails(_lastDiagnostics.GismtToEsm, "Связь с ГИС МТ"));
        ApplyLink(EsmControllerLine, EsmControllerMarker, presentation.EsmToController,
            _diagnosticPresentation.ConnectionDetails(_lastDiagnostics.EsmToController, "Связь с контроллером"));
        ApplyLink(ControllerLmLine, ControllerLmMarker, presentation.ControllerToLm,
            _diagnosticPresentation.ConnectionDetails(_lastDiagnostics.LmConnection, "Связь с ЛМ ЧЗ"));
        ApplyLink(EsmKktLine, EsmKktMarker, presentation.EsmToKkt,
            _diagnosticPresentation.ConnectionDetails(_lastDiagnostics.EsmToKkt, "Связь с ККТ"));
    }

    private void ApplyDetailedProblems(DiagnosticIssuePresentation issue)
    {
        DetailedProblemTitle.Text = issue.Title;
        DetailedProblemReasons.Text = string.IsNullOrWhiteSpace(issue.Recommendation)
            ? issue.Description
            : $"{issue.Description}\n{issue.Recommendation}";
    }

    private void ApplyStandaloneFrame(Border border, Border icon, Ellipse statusDot, TextBlock text, DiagnosticComponentFact fact, string status, string componentName)
    {
        TopologyVisualState state = fact?.State switch
        {
            DiagnosticState.Healthy => TopologyVisualState.Healthy,
            DiagnosticState.Failed => TopologyVisualState.Missing,
            _ => TopologyVisualState.Uncertain
        };
        Brush color = BrushForNodeState(state);
        border.BorderBrush = color;
        border.ToolTip = _diagnosticPresentation.ComponentDetails(fact, componentName);
        SetTopologyIconBrush(icon, color);
        statusDot.Fill = color;
        text.Text = status;
        text.Foreground = color;
    }

    private void ApplySimpleStatus(TopologyPresentation presentation)
    {
        if (_lastDiagnostics != null)
        {
            string description = _diagnosticPresentation.SimpleDescription(_lastDiagnostics.WorkState);
            switch (_lastDiagnostics.WorkState)
            {
                case WorkState.WorkImpossible:
                    SetSimpleStatus("#FDE8EC", "#D91532", "×", "Работа невозможна", description, true, true);
                    return;
                case WorkState.UnableToVerify:
                    SetSimpleStatus("#FFF6D8", "#E3A008", "!", "Не удалось подтвердить готовность",
                        description, false, false);
                    return;
                case WorkState.Attention:
                    SetSimpleStatus("#FFF6D8", "#E3A008", "!", "Требуется внимание", description, true, false);
                    return;
                case WorkState.Ready:
                    SetSimpleStatus("#E8F8F1", "#0E9F6E", "✓", "Всё готово к работе",
                        description, false, false);
                    return;
            }
        }
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
        showFix = showFix && _paidExecutionEnabled;
        showHelp = showHelp && _paidExecutionEnabled;
        SimpleStatusHalo.Fill = BrushFrom(halo);
        SimpleStatusLamp.Fill = BrushFrom(lamp);
        SimpleStatusSymbol.Text = symbol;
        SimpleStatusTitle.Text = title;
        SimpleStatusDescription.Text = description;
        SimpleFixButton.Visibility = showFix ? Visibility.Visible : Visibility.Collapsed;
        SimpleFixButton.IsEnabled = showFix && !_operationRunning && _lastDiagnostics?.Issues.Any(issue => issue.SuggestedFix.HasValue) == true;
        DetailedFixButton.IsEnabled = _paidExecutionEnabled &&
            !_operationRunning && _lastDiagnostics?.Issues.Any(issue => issue.SuggestedFix.HasValue) == true;
        SimpleHelpButton.Visibility = showHelp ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateHeaderWorkState(DiagnosticsSnapshot diagnostics)
    {
        if (_applicationMode != ApplicationMode.Free || diagnostics == null)
            return;

        (string icon, string title, string description, string background, string foreground) = diagnostics.WorkState switch
        {
            WorkState.Ready => ("✓", "Готово к работе", "Локальная проверка завершена", "#E8F8F1", "#0E9F6E"),
            WorkState.Attention => ("!", "Требуется внимание", "Есть рекомендации по локальным компонентам", "#FFF6D8", "#B77900"),
            WorkState.WorkImpossible => ("×", "Работа невозможна", "Один из обязательных компонентов недоступен", "#FDE8EC", "#D91532"),
            _ => ("?", "Не удалось проверить", "Повторите локальную проверку", "#F4F7FB", "#68748A")
        };
        HeaderStatusIcon.Text = icon;
        HeaderStatusTitle.Text = title;
        HeaderStatusDescription.Text = description;
        HeaderStatusBorder.Background = BrushFrom(background);
        HeaderStatusIcon.Foreground = HeaderStatusTitle.Foreground = HeaderStatusDescription.Foreground = BrushFrom(foreground);
    }

    private void ApplyFrame(
        Border border,
        Border icon,
        Ellipse statusDot,
        TextBlock text,
        DiagnosticComponentFact fact,
        TopologyVisualState state,
        string status,
        string componentName)
    {
        Brush color = BrushForNodeState(state);
        border.BorderBrush = color;
        border.BorderThickness = new Thickness(2.2);
        border.ToolTip = _diagnosticPresentation.ComponentDetails(fact, componentName);
        text.Text = status;
        text.Foreground = color;
        SetTopologyIconBrush(icon, color);
        statusDot.Fill = color;
    }

    private static void SetTopologyIconBrush(Border icon, Brush brush)
    {
        icon.Background = brush;
    }

    private static Brush BrushForNodeState(TopologyVisualState state) => state switch
    {
        TopologyVisualState.Healthy => BrushFrom("#0E9F6E"),
        TopologyVisualState.Missing => BrushFrom("#D91532"),
        TopologyVisualState.Ignored => BrushFrom("#94A3B8"),
        _ => BrushFrom("#F4B740")
    };

    private static void ApplyLink(Line line, Border marker, TopologyLinkPresentation presentation, string description)
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
        line.ToolTip = description;
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
        marker.ToolTip = description;
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
        if (!_paidExecutionEnabled) return;
        if (sender is not Button { Tag: string serviceName } || serviceName.EndsWith("*", StringComparison.Ordinal)) return;
        if (_operationRunning) return;
        if (action == ServiceAction.Stop && MessageBox.Show(this,
                $"Остановить службу {serviceName}? Работа связанного компонента будет прервана.",
                "Остановка службы", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        _operationRunning = true;
        ServiceToolsGrid.IsEnabled = false;
        SectionDescription.Text = $"{ServiceActionText(action)} службу «{serviceName}»…";
        try
        {
            var serviceControl = new WindowsServiceControlService(CreateLicenseGuard());
            switch (action)
            {
                case ServiceAction.Start:
                    await serviceControl.StartServiceAsync(serviceName,
                        HonestFlow.Models.Licensing.LicenseOperation.ManageServices, _lifetime.Token);
                    break;
                case ServiceAction.Stop:
                    await serviceControl.StopServiceAsync(serviceName,
                        HonestFlow.Models.Licensing.LicenseOperation.ManageServices, _lifetime.Token);
                    break;
                case ServiceAction.Restart:
                    await serviceControl.RestartServiceAsync(serviceName,
                        HonestFlow.Models.Licensing.LicenseOperation.ManageServices, _lifetime.Token);
                    break;
            }
            await RefreshServiceToolsAsync();
            SectionDescription.Text = $"Служба «{serviceName}»: состояние обновлено.";
        }
        catch (WindowsServiceControlException ex)
        {
            Logger.LogException(ex, $"Service control failed: {serviceName}", nameof(MainWindow));
            await RefreshServiceToolsAsync();
            SectionDescription.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Управление службой", MessageBoxButton.OK,
                ex.Failure == WindowsServiceControlFailure.AccessDenied
                    ? MessageBoxImage.Warning
                    : MessageBoxImage.Information);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { ShowToolError(ex); }
        finally
        {
            ServiceToolsGrid.IsEnabled = true;
            _operationRunning = false;
        }
    }

    private static string ServiceActionText(ServiceAction action) => action switch
    {
        ServiceAction.Start => "Запускаем",
        ServiceAction.Stop => "Останавливаем",
        ServiceAction.Restart => "Перезапускаем",
        _ => "Обновляем"
    };

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
            if (_paidExecutionEnabled)
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
            if (!_paidExecutionEnabled && elevated)
                return;
            if (_paidExecutionEnabled)
                CreateLicenseGuard().Demand(HonestFlow.Models.Licensing.LicenseOperation.OpenLocalTools);
            Process.Start(new ProcessStartInfo(tool) { UseShellExecute = true, Verb = elevated ? "runas" : string.Empty });
        }
        catch (Exception ex) { ShowToolError(ex); }
    }

    private void RemoveEsmGuiDuplicates_Click(object sender, RoutedEventArgs e)
    {
        if (!_paidExecutionEnabled) return;
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
    private async void ConnectService_Click(object sender, RoutedEventArgs e)
    {
        if (_applicationMode != ApplicationMode.Free || _operationRunning || _manualServiceConnectionRunning)
            return;

        _manualServiceConnectionRunning = true;
        _operationRunning = true;
        _silentServiceResumeCancellation?.Cancel();
        ConnectServiceButton.IsEnabled = false;
        bool gateEntered = false;
        try
        {
            await _serviceConnectionGate.WaitAsync(_lifetime.Token);
            gateEntered = true;
            if (_applicationMode != ApplicationMode.Free)
                return;

            ShowServiceConnectionStatus(ServiceConnectionState.Connecting, null);
            var connectionWindow = new StartupWindow { Owner = this };
            bool? connected = connectionWindow.ShowDialog();
            if (connected == true && connectionWindow.ConnectionResult?.Context is ServiceRuntimeContext context)
            {
                await ActivateServiceAsync(context);
                return;
            }

            ShowServiceConnectionStatus(
                connectionWindow.ConnectionState,
                connectionWindow.ConnectionMessage);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "HonestFlow Service connection failed", nameof(MainWindow));
            ShowServiceConnectionStatus(
                ServiceConnectionState.Unavailable,
                "Не удалось подключить HonestFlow Service. HonestFlow Free продолжает работать.");
        }
        finally
        {
            if (gateEntered)
                _serviceConnectionGate.Release();
            _manualServiceConnectionRunning = false;
            _operationRunning = false;
            ConnectServiceButton.IsEnabled = true;
        }
    }

    internal void BeginRememberedServiceResume()
    {
        if (_applicationMode != ApplicationMode.Free || _silentServiceResumeStarted)
            return;

        _silentServiceResumeStarted = true;
        _silentServiceResumeCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        CancellationToken cancellationToken = _silentServiceResumeCancellation.Token;
        Dispatcher.BeginInvoke(
            new Action(() => _ = ResumeRememberedServiceAsync(cancellationToken)),
            DispatcherPriority.ContextIdle);
    }

    private async Task ResumeRememberedServiceAsync(CancellationToken cancellationToken)
    {
        bool gateEntered = false;
        try
        {
            await _serviceConnectionGate.WaitAsync(cancellationToken);
            gateEntered = true;
            if (_applicationMode != ApplicationMode.Free)
                return;

            ServiceConnectionResult result = await RememberedServiceResumeService
                .CreateProduction()
                .TryResumeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.IsActive &&
                result.Context is ServiceRuntimeContext context &&
                _applicationMode == ApplicationMode.Free)
            {
                await ActivateServiceAsync(context);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Background Service resume failed", nameof(MainWindow));
        }
        finally
        {
            if (gateEntered)
                _serviceConnectionGate.Release();
        }
    }

    internal async Task ActivateServiceAsync(ServiceRuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _serviceLifetime?.Cancel();
        _serviceLifetime?.Dispose();
        _serviceLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);

        LicenseObservationSnapshotStore.Instance.SnapshotChanged -= LicenseSnapshotChanged;
        _serviceRuntime = context;
        _startupController = context.Controller;
        _startup = context.Startup;
        _client = context.Client;
        _license = context.Entitlement;
        _deviceId = context.DeviceId;
        _pointStatusRefresh = context.PointStatusRefresh;
        _componentVersionStatusService = context.ComponentVersionStatus;
        _applicationMode = ApplicationMode.Service;
        _paidExecutionEnabled = true;
        _serviceConnectionState = ServiceConnectionState.Active;
        LicenseObservationSnapshotStore.Instance.SnapshotChanged += LicenseSnapshotChanged;
        _autoFixWorkflow = CreateAutoFixWorkflow();

        ApplyServicePresentation();
        await RefreshTopologyAsync();
        _ = RunPeriodicLicenseRefreshAsync(_serviceLifetime.Token);
    }

    private async Task DeactivateServiceAsync(ServiceConnectionState state, string? message, bool logout)
    {
        ServiceRuntimeContext? service = _serviceRuntime;
        if (service is null)
            return;

        _serviceLifetime?.Cancel();
        _serviceLifetime?.Dispose();
        _serviceLifetime = null;
        LicenseObservationSnapshotStore.Instance.SnapshotChanged -= LicenseSnapshotChanged;
        _serviceRuntime = null;
        _paidExecutionEnabled = false;

        if (logout)
        {
            try
            {
                await service.Controller.LogoutAsync(service.Session, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
        }

        _startupController = null;
        _startup = null;
        _client = null;
        _license = null;
        _deviceId = null;
        _applicationMode = ApplicationMode.Free;
        _serviceConnectionState = state;
        _pointStatusRefresh = _localContext?.PointStatusRefresh;
        _componentVersionStatusService = _localContext?.ComponentVersionStatus;

        ApplyFreePresentation();
        await RefreshTopologyAsync();
        ShowServiceConnectionStatus(state, message);
    }
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
    protected override void OnClosed(EventArgs e)
    {
        LicenseObservationSnapshotStore.Instance.SnapshotChanged -= LicenseSnapshotChanged;
        _silentServiceResumeCancellation?.Cancel();
        _silentServiceResumeCancellation?.Dispose();
        _serviceLifetime?.Cancel();
        _serviceLifetime?.Dispose();
        _logTimer.Stop();
        _lifetime.Cancel();
        base.OnClosed(e);
    }

    private sealed class MainWindowProgress : IProgressService
    {
        private readonly MainWindow _window;
        private readonly bool _showInstallationProgress;

        public MainWindowProgress(MainWindow window, bool showInstallationProgress = false)
        {
            _window = window;
            _showInstallationProgress = showInstallationProgress;
        }

        public void SetProgress(int percent, string stepName) => _window.Dispatcher.Invoke(() =>
        {
            int value = Math.Clamp(percent, 0, 100);
            if (_showInstallationProgress)
            {
                if (_window._installationCancellation?.IsCancellationRequested == true) return;
                _window.InstallationProgressBar.Value = value;
                _window.InstallationProgressText.Text = $"{stepName} ({value}%)";
                return;
            }

            _window.SectionOutput.Text = $"{stepName} ({value}%)";
        });
    }

    private sealed class WpfDialogs : IUserDialogService
    {
        private readonly Window _owner;
        private readonly bool _suppressInformationMessages;

        public WpfDialogs(Window owner, bool suppressInformationMessages = false)
        {
            _owner = owner;
            _suppressInformationMessages = suppressInformationMessages;
        }

        public void ShowInformation(string message, string title)
        {
            if (!_suppressInformationMessages)
                Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
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
        public string ActionText { get; init; } = "Переустановить";
        public string ActionTooltip { get; init; } = string.Empty;
    }

    private enum Section { Home, Diagnostics, Components, Log }
}
