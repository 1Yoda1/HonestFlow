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
using HonestFlow.Application.Prerequisites;

namespace HonestFlow
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            System.Windows.Forms.Application.Run(new StartupApplicationContext());
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
                    Logger.Info("Application startup", nameof(Program));
                    int registeredLegacyCaches = new InstallerCacheLocationStore()
                        .RegisterLocations(new[]
                        {
                            AppPaths.LegacyYandexDiskCacheFolder,
                            AppPaths.LegacyRemoteCacheFolder
                        });
                    Logger.Info(
                        $"Event=InstallerCacheLocations Registered={registeredLegacyCaches}",
                        nameof(Program));

                    var deviceIdentityService = new FileDeviceIdentityService(
                        new DpapiDeviceIdentityStateProtector());
                    await deviceIdentityService.GetOrCreateAsync(CancellationToken.None);

                    startupProgress.SetProgress(8, "\u041f\u0440\u043e\u0432\u0435\u0440\u044f\u0435\u043c \u043e\u0431\u043d\u043e\u0432\u043b\u0435\u043d\u0438\u0435 HonestFlow...");
                    var updater = new SelfUpdateService(new WinFormsDialogService(_startupForm));
                    bool updateStarted = await updater.CheckDownloadAndRunUpdateIfNeeded();

                    if (updateStarted)
                    {
                        ExitThread();
                        return;
                    }

                    startupProgress.SetProgress(28, "\u0417\u0430\u0433\u0440\u0443\u0436\u0430\u0435\u043c \u0441\u043f\u0438\u0441\u043a\u0438 \u0442\u043e\u0447\u0435\u043a \u0438 \u0432\u0435\u0440\u0441\u0438\u0438...");
                    var logService = new LogService();
                    var startup = await Task.Run(() =>
                        new ApplicationStartupService(
                            logService,
                            startupProgress,
                            new WinFormsDialogService(_startupForm))
                        .Start());

                    startup.AuthService = LicenseObservationBootstrap.WrapAuthService(startup.AuthService);

                    startup.AuthorizedClient = await AuthenticateSellerAtStartupAsync(startup.AuthService);
                    startup.SellerAuthenticationHandled = true;
                    if (startup.AuthorizedClient != null)
                        await PrepareDotNet10Async(logService);

                    startupProgress.SetProgress(92, "\u041e\u0442\u043a\u0440\u044b\u0432\u0430\u0435\u043c \u0433\u043b\u0430\u0432\u043d\u043e\u0435 \u043e\u043a\u043d\u043e...");
                    var mainForm = new MainForm(startup);
                    mainForm.FormClosed += (sender, args) =>
                    {
                        Logger.Info("Application closed", nameof(Program));
                        ExitThread();
                    };

                    mainForm.Shown += (sender, args) =>
                    {
                        if (!_startupForm.IsDisposed)
                            _startupForm.Close();
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

            private async Task<IPData> AuthenticateSellerAtStartupAsync(IAuthService authService)
            {
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

            private async Task PrepareDotNet10Async(ILogService logService)
            {
                var installer = new DotNetDesktopRuntimeInstaller(logService);
                var progress = new Progress<DotNetRuntimeInstallProgress>(value =>
                {
                    _startupForm.SetProgress(value.Percent, value.Message);
                    _startupForm.ShowPreparationStatus(value.Message, isError: false);
                });

                DotNetRuntimeInstallResult result = await installer.EnsureInstalledAsync(
                    progress,
                    CancellationToken.None);
                Logger.Info(
                    $"Event=DotNet10Preparation Status={result.Status} " +
                    $"ExitCode={result.ExitCode?.ToString() ?? "None"}",
                    nameof(Program));

                _startupForm.SetProgress(100, result.Message);
                _startupForm.ShowPreparationStatus(result.Message, isError: !result.IsSuccess);

                if (result.Status != DotNetRuntimeInstallStatus.AlreadyInstalled)
                    await Task.Delay(result.IsSuccess ? 700 : 1800);
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
