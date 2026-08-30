using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Bootstrap;
using HonestFlow.Application.Core;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.Installation;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Infrastructure.Licensing;
using HonestFlow.Models;

namespace HonestFlow.UI;

public partial class StartupWindow : Window
{
    private static readonly Brush ActiveBlue = BrushFrom("#0967D9");
    private static readonly Brush CompleteGreen = BrushFrom("#0E9F6E");
    private static readonly Brush ErrorRed = BrushFrom("#D91532");
    private static readonly Brush InactiveBorder = BrushFrom("#536985");
    private static readonly Brush InactiveText = BrushFrom("#AFBDD2");
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ApplicationStartupController _controller = new();
    private ApplicationStartupSession? _session;
    private LicenseObservationSnapshot? _restrictedSnapshot;
    private DeviceRegistrationStartupWorkflow? _deviceRegistrationWorkflow;
    private LicenseNotIssuedStartupWorkflow? _licenseNotIssuedWorkflow;
    private LastAuthorizedClientHint? _lastAuthorizedClientHint;
    private readonly string? _accessNotice;
    private StartupPresentationPhase _startupPhase;

    public StartupWindow() : this(null)
    {
    }

    public StartupWindow(string? accessNotice)
    {
        _accessNotice = accessNotice;
        InitializeComponent();
        ApplyStartupPhase(StartupPresentationPhase.Initializing);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _session = await _controller.InitializeAsync(new WpfProgress(this), new WpfDialogs(this), _lifetime.Token);
            _lastAuthorizedClientHint = await _controller.LoadLastAuthorizedClientHintAsync(_lifetime.Token);
            ApplyStartupPhase(StartupPresentationPhase.RememberedAccessChecking);
            LicenseObservationSnapshot? continuation = await _controller
                .TryResumeRegistrationContinuationAsync(_session, _lifetime.Token);
            if (continuation != null)
            {
                await ShowRestrictedAccessAsync(continuation);
                return;
            }
            var resumeProgress = new Progress<LicenseAuthenticationProgress>(ReportAuthenticationProgress);
            LicenseAuthenticationResult resumed = await _controller.TryResumeAsync(
                _session, resumeProgress, _lifetime.Token);
            if (resumed.Client != null)
            {
                if (resumed.LicenseSnapshot?.Decision == LicenseDecision.Allowed)
                {
                    await OpenMainWindowAsync(resumed.Client, resumed.LicenseSnapshot);
                    return;
                }

                await ShowRestrictedAccessAsync(resumed.LicenseSnapshot);
                return;
            }
            ShowLogin();
            if (!string.IsNullOrWhiteSpace(_accessNotice))
            {
                LoginError.Text = _accessNotice;
                LoginError.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.LogException(ex, "WPF startup failed", nameof(StartupWindow));
            if (_startupPhase == StartupPresentationPhase.Initializing)
                ApplyStartupPhase(StartupPresentationPhase.PreparationFailed);
            LoadingStatus.Text = "Ошибка запуска";
            LoadingDescription.Text = ex.Message;
        }
    }

    private void ShowLogin()
    {
        ApplyStartupPhase(StartupPresentationPhase.FreshLogin);
        ShowOnly(LoginPanel);
        LastAuthorizedClientHintPanel.Visibility = string.IsNullOrWhiteSpace(_lastAuthorizedClientHint?.ClientName)
            ? Visibility.Collapsed
            : Visibility.Visible;
        LastAuthorizedClientName.Text = _lastAuthorizedClientHint?.ClientName ?? string.Empty;
        LoginButton.Content = "Войти и продолжить";
        LoginButton.IsEnabled = true;
        PasswordInput.Focus();
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;
        if (string.IsNullOrWhiteSpace(PasswordInput.Password))
        {
            LoginError.Text = "Введите код клиента.";
            LoginError.Visibility = Visibility.Visible;
            PasswordInput.Focus();
            return;
        }

        LoginError.Visibility = Visibility.Collapsed;
        LoginButton.IsEnabled = false;
        ApplyStartupPhase(StartupPresentationPhase.ManualAccessChecking);
        try
        {
            var progress = new Progress<LicenseAuthenticationProgress>(ReportAuthenticationProgress);
            LicenseAuthenticationResult result = await _controller.AuthenticateAsync(
                _session, string.Empty, PasswordInput.Password,
                RememberLoginCheckBox.IsChecked == true, progress, _lifetime.Token);
            if (result.LicenseSnapshot?.Decision == LicenseDecision.DeviceNotRegistered)
            {
                await ShowRestrictedAccessAsync(result.LicenseSnapshot);
                return;
            }
            if (result.Client == null)
            {
                LoginError.Text = "Неверный код клиента.";
                LoginError.Visibility = Visibility.Visible;
                PasswordInput.SelectAll();
                PasswordInput.Focus();
                return;
            }

            if (result.LicenseSnapshot?.Decision == LicenseDecision.Allowed)
            {
                await OpenMainWindowAsync(result.Client, result.LicenseSnapshot);
                return;
            }

            await ShowRestrictedAccessAsync(result.LicenseSnapshot);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (ApiRequestException ex) when (
            !string.IsNullOrWhiteSpace(ApiAuthenticationErrorPresentation.GetMessage(ex)))
        {
            Logger.LogException(ex, "WPF authentication rejected", nameof(StartupWindow));
            LoginError.Text = ApiAuthenticationErrorPresentation.GetMessage(ex);
            LoginError.Visibility = Visibility.Visible;
            PasswordInput.SelectAll();
            PasswordInput.Focus();
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "WPF authentication failed", nameof(StartupWindow));
            LoginError.Text = "Не удалось проверить код клиента. Повторите попытку.";
            LoginError.Visibility = Visibility.Visible;
        }
        finally
        {
            LoginButton.IsEnabled = true;
            LoginButton.Content = "Войти и продолжить";
        }
    }

    private void ReportAuthenticationProgress(LicenseAuthenticationProgress progress)
    {
        if (progress.Stage == LicenseAuthenticationStage.CheckingPassword &&
            _startupPhase != StartupPresentationPhase.RememberedAccessChecking)
        {
            ApplyStartupPhase(StartupPresentationPhase.ManualAccessChecking);
        }
        else if (progress.Stage is LicenseAuthenticationStage.ClientResolved or
                 LicenseAuthenticationStage.CheckingDeviceAndLicense)
        {
            ApplyStartupPhase(StartupPresentationPhase.LicenseChecking);
        }

        LoginButton.Content = progress.Stage switch
        {
            LicenseAuthenticationStage.CheckingPassword => "Проверяем код клиента…",
            LicenseAuthenticationStage.ClientResolved => "Точка найдена…",
            LicenseAuthenticationStage.CheckingDeviceAndLicense => "Проверяем лицензию…",
            _ => "Завершаем проверку…"
        };
    }

    private void ServiceInstallation_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;
        ServiceInstallationButton.IsEnabled = false;
        HttpClient? httpClient = null;
        try
        {
            httpClient = CreateServiceInstallationHttpClient();
            var workflow = new ServiceInstallationAccessWorkflow(
                new ApiServiceInstallationAccessClient(httpClient));
            string architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86";
            string appVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
            var dialog = new ServiceAccessDialog(
                workflow, architecture, appVersion, _lifetime.Token) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Session == null)
            {
                httpClient.Dispose();
                return;
            }

            PasswordInput.Clear();
            var installationWindow = new InstallationModeWindow(
                dialog.Session, httpClient, _session.LogService);
            httpClient = null;
            System.Windows.Application.Current.MainWindow = installationWindow;
            installationWindow.Show();
            Close();
        }
        finally
        {
            httpClient?.Dispose();
            ServiceInstallationButton.IsEnabled = true;
        }
    }

    private static HttpClient CreateServiceInstallationHttpClient()
    {
        string? configuredBaseUrl = Environment.GetEnvironmentVariable("HONESTFLOW_API_BASE_URL");
        return new HttpClient
        {
            BaseAddress = new Uri(string.IsNullOrWhiteSpace(configuredBaseUrl)
                ? "https://api.honestflow.ru/"
                : configuredBaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    private async Task ShowRestrictedAccessAsync(LicenseObservationSnapshot? snapshot)
    {
        _restrictedSnapshot = snapshot;
        _deviceRegistrationWorkflow = null;
        _licenseNotIssuedWorkflow = null;
        if (snapshot?.Decision == LicenseDecision.DeviceNotRegistered)
        {
            await ShowDeviceRegistrationAsync(snapshot);
            return;
        }

        ShowOnly(LicenseMissingPanel);
        SwitchBlockedClientButton.Visibility = Visibility.Collapsed;
        RequestHelpButton.Visibility = Visibility.Visible;

        if (snapshot?.TechnicalCode == "CLIENT_ACCESS_DISABLED")
        {
            ApplyStartupPhase(StartupPresentationPhase.ClientAccessDisabled);
            LicenseMissingTitle.Text = "Доступ к HonestFlow отключён";
            LicenseMissingDescription.Text = "Доступ к HonestFlow для этого клиента отключён.";
            RegistrationStatus.Text = string.Empty;
            RetryLicenseButton.Visibility = Visibility.Collapsed;
            RequestHelpButton.Visibility = Visibility.Collapsed;
            SwitchBlockedClientButton.Visibility = Visibility.Visible;
            SetLicenseClientContext(snapshot.ClientName);
            return;
        }

        if (snapshot?.Decision == LicenseDecision.LicenseNotIssued &&
            _session?.Startup.AuthorizedClient != null &&
            _session.Startup.AuthService is ILicenseObservationRefresher licenseRefresher)
        {
            ApplyStartupPhase(StartupPresentationPhase.LicenseNotIssued);
            LicenseMissingTitle.Text = "Лицензия ещё не выдана";
            LicenseMissingDescription.Text =
                "Устройство уже зарегистрировано. После выдачи лицензии запуск продолжится без повторного входа.";
            RegistrationStatus.Text = LicenseNotIssuedStartupWorkflow.WaitingMessage;
            SetLicenseClientContext(_session.Startup.AuthorizedClient?.Name ?? snapshot.ClientName);
            RetryLicenseButton.Visibility = Visibility.Visible;
            _licenseNotIssuedWorkflow = new LicenseNotIssuedStartupWorkflow(
                licenseRefresher,
                _session.Startup.AuthorizedClient);
            return;
        }

        ApplyStartupPhase(StartupStagePresentationMapper.PhaseForLicense(snapshot));

        LicenseMissingTitle.Text = "Доступ ограничен";
        LicenseMissingDescription.Text =
            "HonestFlow не может продолжить запуск при текущем состоянии лицензии.";
        RetryLicenseButton.Visibility = Visibility.Collapsed;
        SetLicenseClientContext(_session?.Startup.AuthorizedClient?.Name ?? snapshot?.ClientName);
        RegistrationStatus.Text = string.IsNullOrWhiteSpace(snapshot?.Message)
            ? "Лицензия не разрешает вход. Запросите помощь, чтобы специалист проверил доступ."
            : snapshot.Message;
    }

    private async Task ShowDeviceRegistrationAsync(LicenseObservationSnapshot snapshot)
    {
        ApplyStartupPhase(StartupPresentationPhase.DeviceAwaitingAddress);
        _restrictedSnapshot = snapshot;
        _deviceRegistrationWorkflow = CreateDeviceRegistrationWorkflow(
            string.Equals(snapshot.TechnicalCode, "REGISTRATION_CONTINUATION", StringComparison.Ordinal));
        DeviceRegistrationStartupResult state = await _deviceRegistrationWorkflow.CheckAsync(_lifetime.Token);
        ShowOnly(DeviceRegistrationPanel);
        await ApplyDeviceRegistrationStateAsync(state);
    }

    private DeviceRegistrationStartupWorkflow CreateDeviceRegistrationWorkflow(bool resumedContinuation)
    {
        if (_session?.Startup.AuthService is not IApiCredentialAuthService authentication ||
            _session.Startup.AuthService is not IApiSessionProvider provider ||
            provider.ApiSessionService is not IApiSessionRefresher sessionRefresher)
        {
            throw new InvalidOperationException("API registration session is unavailable.");
        }

        IApiSessionService session = provider.ApiSessionService;
        return new DeviceRegistrationStartupWorkflow(
            new DeviceRegistrationWorkflow(new DeviceRegistrationCoordinator(
                new DeviceRegistrationRequestService(),
                new ApiDeviceRegistrationRequestSender(session),
                new DpapiDeviceRegistrationDeliveryStateStore())),
            new ApiDeviceRegistrationStatusProvider(session),
            sessionRefresher,
            authentication,
            session as IApiSessionPersistenceController,
            resumedContinuation);
    }

    private async Task ApplyDeviceRegistrationStateAsync(DeviceRegistrationStartupResult state)
    {
        ApplyStartupPhase(StartupStagePresentationMapper.PhaseForRegistration(state.State));
        if (state.State == DeviceRegistrationStartupState.SessionInvalid)
        {
            ShowLogin();
            LoginError.Text = state.Message;
            LoginError.Visibility = Visibility.Visible;
            return;
        }

        if (state.State == DeviceRegistrationStartupState.Allowed && state.Authentication?.Client != null)
        {
            _session!.Startup.AuthorizedClient = state.Authentication.Client;
            _session.Startup.SellerAuthenticationHandled = true;
            await OpenMainWindowAsync(state.Authentication.Client, state.Authentication.LicenseSnapshot);
            return;
        }

        if (state.State == DeviceRegistrationStartupState.ApprovedNotReady &&
            state.Authentication?.Client != null)
        {
            _session!.Startup.AuthorizedClient = state.Authentication.Client;
            _session.Startup.SellerAuthenticationHandled = true;
            await ShowRestrictedAccessAsync(state.Authentication.LicenseSnapshot);
            return;
        }

        DeviceRegistrationPresentation presentation = DeviceRegistrationPresentationMapper.Create(
            state, _restrictedSnapshot?.ClientName);
        RegistrationTitle.Text = presentation.Title;
        RegistrationDescription.Text = presentation.Description;
        RegistrationAddressLabel.Visibility = presentation.ShowAddressEntry ? Visibility.Visible : Visibility.Collapsed;
        RegistrationAddressInput.Visibility = presentation.ShowAddressEntry ? Visibility.Visible : Visibility.Collapsed;
        SubmitRegistrationButton.Visibility = presentation.ShowSubmit ? Visibility.Visible : Visibility.Collapsed;
        SubmitRegistrationButton.Content = presentation.SubmitText ?? string.Empty;
        CheckRegistrationButton.Visibility = presentation.ShowCheckStatus ? Visibility.Visible : Visibility.Collapsed;
        SwitchClientButton.Visibility = presentation.ShowSwitchClient ? Visibility.Visible : Visibility.Collapsed;
        RegistrationError.Visibility = state.State == DeviceRegistrationStartupState.InvalidAddress
            ? Visibility.Visible
            : Visibility.Collapsed;
        RegistrationError.Text = state.State == DeviceRegistrationStartupState.InvalidAddress
            ? state.Message
            : string.Empty;
        DeviceRegistrationStatus.Text = !string.IsNullOrWhiteSpace(presentation.GuidanceText)
            ? presentation.GuidanceText
            : state.State is DeviceRegistrationStartupState.Pending or DeviceRegistrationStartupState.Rejected
                ? string.Empty
                : state.Message;
        ApplyRegistrationContext(presentation);
        if (presentation.ShowAddressEntry)
            RegistrationAddressInput.Focus();

        await Task.CompletedTask;
    }

    private void ApplyRegistrationContext(DeviceRegistrationPresentation presentation)
    {
        RegistrationContextPanel.Visibility = presentation.HasContext ? Visibility.Visible : Visibility.Collapsed;
        SetContextRow(RegistrationClientRow, RegistrationClientName, presentation.ClientName);
        SetContextRow(RegistrationRequestedAtRow, RegistrationRequestedAt, presentation.RequestedAtText);
        SetContextRow(RegistrationStatusRow, RegistrationStatusValue, presentation.StatusText);
        SetContextRow(RegistrationReasonRow, RegistrationReason, presentation.RejectionReason);
    }

    private void SetLicenseClientContext(string? clientName)
    {
        LicenseContextPanel.Visibility = string.IsNullOrWhiteSpace(clientName) ? Visibility.Collapsed : Visibility.Visible;
        LicenseClientName.Text = clientName ?? string.Empty;
    }

    private static void SetContextRow(StackPanel row, TextBlock value, string? text)
    {
        row.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        value.Text = text ?? string.Empty;
    }

    private async void SubmitRegistration_Click(object sender, RoutedEventArgs e)
    {
        string address = RegistrationAddressInput.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(address))
        {
            RegistrationError.Text = "Введите физический адрес торговой точки.";
            RegistrationError.Visibility = Visibility.Visible;
            RegistrationAddressInput.Focus();
            return;
        }
        if (address.Length > 300)
        {
            RegistrationError.Text = "Адрес не должен быть длиннее 300 символов.";
            RegistrationError.Visibility = Visibility.Visible;
            RegistrationAddressInput.Focus();
            return;
        }

        RegistrationError.Visibility = Visibility.Collapsed;
        DeviceRegistrationStatus.Text = "Отправляем заявку на регистрацию…";
        await RunDeviceRegistrationActionAsync(async workflow =>
        {
            return await workflow.SubmitAsync(
                _restrictedSnapshot!, address, _lifetime.Token);
        });
    }

    private async void CheckRegistration_Click(object sender, RoutedEventArgs e) =>
        await RunDeviceRegistrationActionAsync(workflow => workflow.CheckAsync(_lifetime.Token));

    private async void SwitchClient_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;

        SwitchClientButton.IsEnabled = false;
        try
        {
            await _controller.LogoutAsync(_session, _lifetime.Token);
            _restrictedSnapshot = null;
            _deviceRegistrationWorkflow = null;
            RegistrationAddressInput.Text = string.Empty;
            ShowLogin();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.LogException(ex, "WPF registration switch-client failed", nameof(StartupWindow));
            DeviceRegistrationStatus.Text = "Не удалось очистить текущую сессию. Попробуйте снова.";
        }
        finally
        {
            SwitchClientButton.IsEnabled = true;
        }
    }

    private async void SwitchBlockedClient_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;

        SwitchBlockedClientButton.IsEnabled = false;
        try
        {
            await _controller.LogoutAsync(_session, _lifetime.Token);
            _restrictedSnapshot = null;
            _deviceRegistrationWorkflow = null;
            RegistrationAddressInput.Text = string.Empty;
            ShowLogin();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.LogException(ex, "WPF blocked-client switch failed", nameof(StartupWindow));
            RegistrationStatus.Text = "Не удалось очистить текущую сессию. Попробуйте снова.";
        }
        finally
        {
            SwitchBlockedClientButton.IsEnabled = true;
        }
    }

    private void ShowOnly(UIElement panel)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Collapsed;
        DeviceRegistrationPanel.Visibility = Visibility.Collapsed;
        LicenseMissingPanel.Visibility = Visibility.Collapsed;
        panel.Visibility = Visibility.Visible;
    }

    private async void RetryLicense_Click(object sender, RoutedEventArgs e)
    {
        if (_licenseNotIssuedWorkflow == null) return;

        RetryLicenseButton.IsEnabled = false;
        RegistrationStatus.Text = "Проверяем лицензию…";
        ApplyStartupPhase(StartupPresentationPhase.LicenseChecking);
        try
        {
            LicenseNotIssuedStartupResult result = await _licenseNotIssuedWorkflow.CheckAsync(_lifetime.Token);
            if (result.IsAllowed && result.Authentication?.Client != null)
            {
                _session!.Startup.AuthorizedClient = result.Authentication.Client;
                _session.Startup.SellerAuthenticationHandled = true;
                await OpenMainWindowAsync(result.Authentication.Client, result.Authentication.LicenseSnapshot);
                return;
            }

            if (result.Snapshot != null)
            {
                _restrictedSnapshot = result.Snapshot;
                ApplyStartupPhase(StartupStagePresentationMapper.PhaseForLicense(result.Snapshot));
            }
            RegistrationStatus.Text = result.Message;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.LogException(ex, "WPF license retry failed", nameof(StartupWindow));
            RegistrationStatus.Text = "Не удалось проверить лицензию. Попробуйте проверить снова.";
        }
        finally
        {
            RetryLicenseButton.IsEnabled = true;
        }
    }

    private async Task RunDeviceRegistrationActionAsync(
        Func<DeviceRegistrationStartupWorkflow, Task<DeviceRegistrationStartupResult>> action)
    {
        if (_deviceRegistrationWorkflow == null) return;

        SubmitRegistrationButton.IsEnabled = false;
        CheckRegistrationButton.IsEnabled = false;
        try
        {
            await ApplyDeviceRegistrationStateAsync(await action(_deviceRegistrationWorkflow));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.LogException(ex, "WPF device registration failed", nameof(StartupWindow));
            ApplyStartupPhase(StartupPresentationPhase.DeviceStatusUnavailable);
            DeviceRegistrationStatus.Text = "Не удалось выполнить операцию. Попробуйте проверить снова.";
        }
        finally
        {
            SubmitRegistrationButton.IsEnabled = true;
            CheckRegistrationButton.IsEnabled = true;
        }
    }

    private async void RequestHelp_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _restrictedSnapshot == null) return;
        RequestHelpButton.IsEnabled = false;
        RegistrationStatus.Text = "Отправляем запрос помощи…";
        try
        {
            await _controller.SendHelpRequestAsync(_session, _restrictedSnapshot, _lifetime.Token);
            RegistrationStatus.Text = "Запрос помощи отправлен. Специалист увидит заявку и свяжется с вами.";
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "WPF help request failed", nameof(StartupWindow));
            RegistrationStatus.Text = ex.Message;
            RequestHelpButton.IsEnabled = true;
        }
    }

    private async Task OpenMainWindowAsync(IPData client, LicenseObservationSnapshot? snapshot)
    {
        ApplyStartupPhase(StartupStagePresentationMapper.PhaseForLicense(snapshot));
        var compactWindow = new CompactMainWindow(_session!, client, snapshot);
        System.Windows.Application.Current.MainWindow = compactWindow;
        compactWindow.Show();
        await _controller.SaveLastAuthorizedClientHintAsync(client, snapshot!, CancellationToken.None);
        ApplyStartupPhase(StartupPresentationPhase.Launched);
        Close();
    }

    private void ApplyStartupPhase(StartupPresentationPhase phase)
    {
        _startupPhase = phase;
        StartupStagePresentation presentation = StartupStagePresentationMapper.Create(phase);
        SetStep(PreparationStepBadge, PreparationStepText, PreparationStepLabel, "1", presentation.Preparation);
        SetStep(AccessStepBadge, AccessStepText, AccessStepLabel, "2", presentation.Access);
        SetStep(DeviceStepBadge, DeviceStepText, DeviceStepLabel, "3", presentation.Device);
        SetStep(LicenseStepBadge, LicenseStepText, LicenseStepLabel, "4", presentation.License);
        SetStep(LaunchStepBadge, LaunchStepText, LaunchStepLabel, "5", presentation.Launch);
        LoadingStatus.Text = presentation.StatusText ?? string.Empty;
    }

    private static void SetStep(
        Border badge,
        TextBlock number,
        TextBlock label,
        string stageNumber,
        StartupStageVisualState state)
    {
        bool active = state == StartupStageVisualState.Active;
        bool complete = state == StartupStageVisualState.Complete;
        bool error = state == StartupStageVisualState.Error;
        badge.Background = active ? ActiveBlue : complete ? CompleteGreen : error ? ErrorRed : Brushes.Transparent;
        badge.BorderBrush = active ? ActiveBlue : complete ? CompleteGreen : error ? ErrorRed : InactiveBorder;
        number.Text = complete ? "✓" : error ? "!" : stageNumber;
        number.Foreground = state == StartupStageVisualState.Inactive ? InactiveText : Brushes.White;
        label.Foreground = state switch
        {
            StartupStageVisualState.Inactive => InactiveText,
            StartupStageVisualState.Complete => BrushFrom("#DCE8F7"),
            StartupStageVisualState.Error => BrushFrom("#FFD5DC"),
            _ => Brushes.White
        };
    }

    private static Brush BrushFrom(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) ToggleMaximize(); else DragMove(); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    protected override void OnClosed(EventArgs e) { _lifetime.Cancel(); base.OnClosed(e); }

    private sealed class WpfProgress : IProgressService
    {
        private readonly StartupWindow _window;
        public WpfProgress(StartupWindow window) => _window = window;
        public void SetProgress(int percent, string stepName) => _window.Dispatcher.Invoke(() =>
            _window.LoadingStatus.Text = "Подготавливаем HonestFlow…");
    }

    private sealed class WpfDialogs : IUserDialogService
    {
        private readonly Window _owner;
        public WpfDialogs(Window owner) => _owner = owner;
        public void ShowInformation(string message, string title) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        public void ShowWarning(string message, string title) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        public void ShowError(string message, string title) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        public bool Confirm(string message, string title, UserDialogIcon icon = UserDialogIcon.Warning) => Show(message, title, MessageBoxButton.YesNo, icon == UserDialogIcon.Error ? MessageBoxImage.Error : MessageBoxImage.Warning) == MessageBoxResult.Yes;
        private MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage icon) => _owner.Dispatcher.Invoke(() => MessageBox.Show(_owner, message, title, buttons, icon));
    }
}
