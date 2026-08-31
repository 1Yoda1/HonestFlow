using System;
using System.Threading;
using System.Windows;
using HonestFlow.Application.Bootstrap;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Infrastructure.Updates;
using HonestFlow.UI;

namespace HonestFlow;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\HonestFlow.SingleInstance.v3";
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            MessageBox.Show("HonestFlow уже запущен.", "HonestFlow", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        try
        {
            var selfUpdate = new SelfUpdateService(
                new PublicHonestFlowUpdateClient(),
                new ApplicationDialogs());
            var startup = new FreeApplicationStartup(
                selfUpdate.CheckDownloadAndRunUpdateIfNeeded,
                new LocalApplicationBootstrap().Create);
            FreeApplicationStartupResult result = await startup.StartAsync(CancellationToken.None);
            if (result.UpdateStarted)
                return;

            var window = new MainWindow(result.Context);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Free application startup failed", nameof(App));
            MessageBox.Show(
                "HonestFlow не удалось запустить локальную диагностику. Подробности записаны в журнал.",
                "Ошибка запуска HonestFlow",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Info("WPF application closed", nameof(App));
        Logger.Shutdown();
        if (_ownsSingleInstanceMutex)
            _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private sealed class ApplicationDialogs : IUserDialogService
    {
        public void ShowInformation(string message, string title) =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

        public void ShowWarning(string message, string title) =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

        public void ShowError(string message, string title) =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

        public bool Confirm(string message, string title, UserDialogIcon icon = UserDialogIcon.Warning) =>
            MessageBox.Show(message, title, MessageBoxButton.YesNo,
                icon == UserDialogIcon.Error ? MessageBoxImage.Error : MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
}
