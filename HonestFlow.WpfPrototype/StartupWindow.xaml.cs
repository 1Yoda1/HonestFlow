using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Bootstrap;
using HonestFlow.Application.Core;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Infrastructure.Licensing;
using HonestFlow.Models;

namespace HonestFlow.WpfPrototype;

public partial class StartupWindow : Window
{
    private static readonly Brush ActiveBlue = BrushFrom("#0967D9");
    private static readonly Brush CompleteGreen = BrushFrom("#0E9F6E");
    private static readonly Brush InactiveBorder = BrushFrom("#536985");
    private static readonly Brush InactiveText = BrushFrom("#AFBDD2");
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ApplicationStartupController _controller = new();
    private ApplicationStartupSession? _session;
    private LicenseObservationSnapshot? _restrictedSnapshot;
    private DeviceRegistrationStartupWorkflow? _deviceRegistrationWorkflow;
    private readonly string? _accessNotice;

    public StartupWindow() : this(null)
    {
    }

    public StartupWindow(string? accessNotice)
    {
        _accessNotice = accessNotice;
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _session = await _controller.InitializeAsync(new WpfProgress(this), new WpfDialogs(this), _lifetime.Token);
            StartupProgress.Value = 100;
            LoadingPercent.Text = "100%";
            LoadingStatus.Text = "Готово";
            await Task.Delay(250, _lifetime.Token);
            var resumeProgress = new Progress<LicenseAuthenticationProgress>(ReportAuthenticationProgress);
            LicenseAuthenticationResult resumed = await _controller.TryResumeAsync(
                _session, resumeProgress, _lifetime.Token);
            if (resumed.Client != null)
            {
                if (resumed.LicenseSnapshot?.Decision == LicenseDecision.Allowed)
                {
                    var mainWindow = new MainWindow(_session.Startup, resumed.Client, resumed.LicenseSnapshot);
                    System.Windows.Application.Current.MainWindow = mainWindow;
                    mainWindow.Show();
                    Close();
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
            LoadingStatus.Text = "Ошибка запуска";
            LoadingDescription.Text = ex.Message;
            StartupProgress.Foreground = BrushFrom("#D91532");
        }
    }

    private void ShowLogin()
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        LicenseMissingPanel.Visibility = Visibility.Collapsed;
        DeviceRegistrationPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Visible;
        SetStep(LoginStepBadge, LoginStepText, LoginStepLabel, StepState.Active);
        PasswordInput.Focus();
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;
        if (string.IsNullOrWhiteSpace(PasswordInput.Password))
        {
            LoginError.Text = "Введите пароль точки.";
            LoginError.Visibility = Visibility.Visible;
            PasswordInput.Focus();
            return;
        }

        LoginError.Visibility = Visibility.Collapsed;
        LoginButton.IsEnabled = false;
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
                LoginError.Text = "Неверный пароль точки. Попробуйте ещё раз.";
                LoginError.Visibility = Visibility.Visible;
                PasswordInput.SelectAll();
                PasswordInput.Focus();
                return;
            }

            if (result.LicenseSnapshot?.Decision == LicenseDecision.Allowed)
            {
                var mainWindow = new MainWindow(_session.Startup, result.Client, result.LicenseSnapshot);
                System.Windows.Application.Current.MainWindow = mainWindow;
                mainWindow.Show();
                Close();
                return;
            }

            await ShowRestrictedAccessAsync(result.LicenseSnapshot);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.LogException(ex, "WPF authentication failed", nameof(StartupWindow));
            LoginError.Text = "Не удалось проверить доступ. Повторите попытку.";
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
        LoginButton.Content = progress.Stage switch
        {
            LicenseAuthenticationStage.CheckingPassword => "Проверяем пароль…",
            LicenseAuthenticationStage.ClientResolved => "Точка найдена…",
            LicenseAuthenticationStage.CheckingDeviceAndLicense => "Проверяем лицензию…",
            _ => "Завершаем проверку…"
        };
        if (progress.Stage != LicenseAuthenticationStage.CheckingPassword)
            SetStep(LoginStepBadge, LoginStepText, LoginStepLabel, StepState.Complete);
        if (progress.Stage == LicenseAuthenticationStage.CheckingDeviceAndLicense)
            SetStep(LicenseStepBadge, LicenseStepText, LicenseStepLabel, StepState.Active);
    }

    private async Task ShowRestrictedAccessAsync(LicenseObservationSnapshot? snapshot)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Collapsed;
        LicenseMissingPanel.Visibility = Visibility.Visible;
        _restrictedSnapshot = snapshot;
        if (snapshot?.Decision == LicenseDecision.DeviceNotRegistered)
        {
            await ShowDeviceRegistrationAsync(snapshot);
            return;
        }
        if (snapshot?.Decision != LicenseDecision.DeviceNotRegistered)
        {
            RegistrationStatus.Text = string.IsNullOrWhiteSpace(snapshot?.Message)
                ? "Лицензия не разрешает вход. Запросите помощь, чтобы специалист проверил доступ."
                : snapshot.Message;
            return;
        }

    }

    private async Task ShowDeviceRegistrationAsync(LicenseObservationSnapshot snapshot)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Collapsed;
        LicenseMissingPanel.Visibility = Visibility.Collapsed;
        DeviceRegistrationPanel.Visibility = Visibility.Visible;
        _restrictedSnapshot = snapshot;
        _deviceRegistrationWorkflow = CreateDeviceRegistrationWorkflow();
        await ApplyDeviceRegistrationStateAsync(await _deviceRegistrationWorkflow.CheckAsync(_lifetime.Token));
    }

    private DeviceRegistrationStartupWorkflow CreateDeviceRegistrationWorkflow()
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
            authentication);
    }

    private async Task ApplyDeviceRegistrationStateAsync(DeviceRegistrationStartupResult state)
    {
        if (state.State == DeviceRegistrationStartupState.Allowed && state.Authentication?.Client != null)
        {
            _session!.Startup.AuthorizedClient = state.Authentication.Client;
            _session.Startup.SellerAuthenticationHandled = true;
            var mainWindow = new MainWindow(_session.Startup, state.Authentication.Client,
                state.Authentication.LicenseSnapshot);
            System.Windows.Application.Current.MainWindow = mainWindow;
            mainWindow.Show();
            Close();
            return;
        }

        bool canSubmitAddress = state.CanSubmitAddress;
        RegistrationAddressLabel.Visibility = canSubmitAddress ? Visibility.Visible : Visibility.Collapsed;
        RegistrationAddressInput.Visibility = canSubmitAddress ? Visibility.Visible : Visibility.Collapsed;
        SubmitRegistrationButton.Visibility = canSubmitAddress ? Visibility.Visible : Visibility.Collapsed;
        RegistrationError.Visibility = state.State == DeviceRegistrationStartupState.InvalidAddress
            ? Visibility.Visible
            : Visibility.Collapsed;
        RegistrationError.Text = state.State == DeviceRegistrationStartupState.InvalidAddress
            ? state.Message
            : string.Empty;
        DeviceRegistrationStatus.Text = state.Message;
        CheckRegistrationButton.Visibility = Visibility.Visible;
        if (canSubmitAddress)
            RegistrationAddressInput.Focus();

        await Task.CompletedTask;
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

        await RunDeviceRegistrationActionAsync(async workflow =>
        {
            DeviceRegistrationStartupResult state = await workflow.SubmitAsync(
                _restrictedSnapshot!, address, _lifetime.Token);
            return state.State == DeviceRegistrationStartupState.Pending
                ? await workflow.CheckAsync(_lifetime.Token)
                : state;
        });
    }

    private async void CheckRegistration_Click(object sender, RoutedEventArgs e) =>
        await RunDeviceRegistrationActionAsync(workflow => workflow.CheckAsync(_lifetime.Token));

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

    private static void SetStep(Border badge, TextBlock number, TextBlock label, StepState state)
    {
        bool complete = state == StepState.Complete;
        badge.Background = state == StepState.Active ? ActiveBlue : complete ? CompleteGreen : Brushes.Transparent;
        badge.BorderBrush = state == StepState.Active ? ActiveBlue : complete ? CompleteGreen : InactiveBorder;
        number.Text = complete ? "✓" : number.Text;
        number.Foreground = state == StepState.Inactive ? InactiveText : Brushes.White;
        label.Foreground = state == StepState.Inactive ? InactiveText : complete ? BrushFrom("#DCE8F7") : Brushes.White;
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
        {
            int safe = Math.Clamp(percent, 0, 100);
            _window.StartupProgress.Value = safe;
            _window.LoadingPercent.Text = $"{safe}%";
            _window.LoadingStatus.Text = stepName;
        });
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

    private enum StepState { Inactive, Active, Complete }
}
