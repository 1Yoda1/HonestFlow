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
