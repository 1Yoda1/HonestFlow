using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using HonestFlow.Application.Bootstrap;
using HonestFlow.Application.Core;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Infrastructure.Updates;
using HonestFlow.Infrastructure.DeviceIdentity;
using System.Threading;
using HonestFlow.Infrastructure.Licensing;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Application.Auth;
using HonestFlow.Models;
using HonestFlow.Application.RemoteAccess;

namespace HonestFlow
{
    internal static class Program
    {
        private const string SingleInstanceMutexName = @"Local\HonestFlow.SingleInstance.v3";

        [STAThread]
        private static void Main()
        {
            if (!ApplicationSingleInstance.TryAcquire(
                    SingleInstanceMutexName,
                    out ApplicationSingleInstance singleInstance))
            {
                MessageBox.Show(
                    "HonestFlow уже запущен.",
                    "HonestFlow",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            using (singleInstance)
            {
                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                System.Windows.Forms.Application.Run(new StartupApplicationContext());
            }
        }

        private sealed class StartupApplicationContext : ApplicationContext
        {
            private readonly StartupProgressForm _startupForm;

            public StartupApplicationContext()
            {
                _startupForm = new StartupProgressForm();
                _startupForm.Shown += async (sender, args) => await StartApplicationAsync();
                _startupForm.Show();
            }

            private async Task StartApplicationAsync()
            {
                var startupProgress = new StartupProgressService(_startupForm);

                try
                {
                    Logger.Initialize();
                    int registeredLegacyCaches = new InstallerCacheLocationStore()
                        .RegisterLocations(new[]
                        {
                            AppPaths.LegacyYandexDiskCacheFolder,
                            AppPaths.LegacyRemoteCacheFolder
                        });
                    if (registeredLegacyCaches > 0)
                    {
                        Logger.Info(
                            $"Зарегистрировано папок кэша установщиков: {registeredLegacyCaches}",
                            nameof(Program));
                    }

                    var deviceIdentityService = new FileDeviceIdentityService(
                        new DpapiDeviceIdentityStateProtector());
                    await deviceIdentityService.GetOrCreateAsync(CancellationToken.None);

                    startupProgress.SetProgress(28, "\u0417\u0430\u0433\u0440\u0443\u0436\u0430\u0435\u043c \u0441\u043f\u0438\u0441\u043a\u0438 \u0442\u043e\u0447\u0435\u043a \u0438 \u0432\u0435\u0440\u0441\u0438\u0438...");
                    var logService = new LogService();
                    var startup = await Task.Run(() =>
                        new ApplicationStartupService(
                            logService,
                            startupProgress,
                            new WinFormsDialogService(_startupForm))
                        .Start());

                    startup.AuthService = LicenseObservationBootstrap.WrapAuthService(startup.AuthService);

                    startup.AuthorizedClient = await AuthenticateSellerAtStartupAsync(startup, logService);
                    startup.SellerAuthenticationHandled = true;
                    startupProgress.SetProgress(92, "\u041e\u0442\u043a\u0440\u044b\u0432\u0430\u0435\u043c \u0433\u043b\u0430\u0432\u043d\u043e\u0435 \u043e\u043a\u043d\u043e...");
                    var mainForm = new MainForm(startup);
                    mainForm.FormClosed += (sender, args) =>
                    {
                        Logger.Info("Application closed", nameof(Program));
                        Logger.Shutdown();
                        ExitThread();
                    };

                    mainForm.Shown += async (sender, args) =>
                    {
                        if (!_startupForm.IsDisposed)
                            _startupForm.Close();

                        var updater = new SelfUpdateService(new WinFormsDialogService(mainForm));
                        if (await updater.CheckDownloadAndRunUpdateIfNeeded())
                            mainForm.Close();
                    };

                    mainForm.Show();
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "Critical startup error", nameof(Program));

                    MessageBox.Show(
                        _startupForm,
                        $"\u041a\u0440\u0438\u0442\u0438\u0447\u0435\u0441\u043a\u0430\u044f \u043e\u0448\u0438\u0431\u043a\u0430:\n{ex.Message}\n\n\u041b\u043e\u0433: {Logger.GetLogPath()}",
                        "HonestFlow",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);

                    ExitThread();
                }
            }

            private async Task<IPData> AuthenticateSellerAtStartupAsync(
                StartupResult startup,
                ILogService logService)
            {
                IAuthService authService = startup.AuthService;
                IPData rememberedClient = await TryRestoreRememberedSellerAsync(
                    startup,
                    logService);
                if (rememberedClient != null)
                    return rememberedClient;

                string errorMessage = null;
                while (true)
                {
                    string password = await _startupForm.RequestSellerPasswordAsync(errorMessage);
                    if (password == null)
                        return null;

                    try
                    {
                        IPData client;
                        if (authService is ILicenseAuthenticatingAuthService licenseAuth)
                        {
                            var progress = new Progress<LicenseAuthenticationProgress>(
                                _startupForm.ReportLicenseAuthentication);
                            LicenseAuthenticationResult result = await licenseAuth.AuthenticateAsync(
                                password,
                                progress,
                                CancellationToken.None);
                            client = result.Client;
                        }
                        else
                        {
                            client = authService.Authenticate(password);
                        }

                        if (client == null)
                        {
                            errorMessage = "Неверный пароль продавца. Попробуйте ещё раз.";
                            continue;
                        }

                        if (startup.UseRemoteConfigMode)
                        {
                            try
                            {
                                new AuthorizedClientCache().Save(client);
                            }
                            catch (Exception ex)
                            {
                                Logger.Warning(
                                    $"Event=AuthorizedClientCacheWriteFailed ErrorType={ex.GetType().Name}",
                                    nameof(Program));
                            }
                        }

                        _startupForm.ShowAuthenticationSuccess(client.Name);
                        await Task.Delay(450);
                        return client;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(
                            $"Event=StartupSellerAuthentication Status=Failed ErrorType={ex.GetType().Name}",
                            nameof(Program));
                        errorMessage = "Не удалось проверить доступ. Повторите попытку.";
                    }
                }
            }

            private async Task<IPData> TryRestoreRememberedSellerAsync(
                StartupResult startup,
                ILogService logService)
            {
                LastAuthorizedClientState remembered = new RuDesktopService(logService)
                    .GetLastAuthorizedClient();
                if (string.IsNullOrWhiteSpace(remembered?.ClientId))
                    return null;

                IPData client = startup.Ips?.Find(candidate =>
                    candidate != null &&
                    string.Equals(
                        candidate.ClientId,
                        remembered.ClientId,
                        StringComparison.Ordinal));
                if (client == null)
                {
                    Logger.Info(
                        "Event=RememberedSellerLogin Status=ClientNotFound",
                        nameof(Program));
                    return null;
                }

                if (!RuDesktopService.IsRememberedAuthorizationCurrent(remembered, client))
                {
                    Logger.Info(
                        "Event=RememberedSellerLogin Status=PasswordChangedOrUnverified",
                        nameof(Program));
                    return null;
                }

                bool continueRememberedLogin = await _startupForm
                    .RequestRememberedSellerConfirmationAsync(client.Name);
                if (!continueRememberedLogin)
                    return null;

                try
                {
                    _startupForm.ShowPreparationStatus(
                        $"Сохранённый вход для точки «{client.Name}» подтверждён. Проверяем лицензию...",
                        isError: false);
                    if (startup.AuthService is ILicenseObservationRefresher refresher)
                    {
                        var progress = new Progress<LicenseAuthenticationProgress>(
                            _startupForm.ReportLicenseAuthentication);
                        await refresher.RefreshLicenseAsync(
                            client,
                            progress,
                            CancellationToken.None);
                    }

                    Logger.Info(
                        "Event=RememberedSellerLogin Status=Success",
                        nameof(Program));
                    _startupForm.ShowAuthenticationSuccess(client.Name);
                    await Task.Delay(450);
                    return client;
                }
                catch (Exception ex)
                {
                    Logger.Warning(
                        $"Event=RememberedSellerLogin Status=Failed ErrorType={ex.GetType().Name}",
                        nameof(Program));
                    _startupForm.ShowPreparationStatus(
                        "Сохранённый вход не подтверждён. Введите актуальный пароль.",
                        isError: true);
                    return null;
                }
            }
        }

        private sealed class StartupProgressService : IProgressService
        {
            private readonly StartupProgressForm _form;

            public StartupProgressService(StartupProgressForm form)
            {
                _form = form;
            }

            public void SetProgress(int percent, string stepName)
            {
                _form.SetProgress(percent, stepName);
            }
        }
    }
}
