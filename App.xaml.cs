using System;
using System.Threading;
using System.Windows;
using HonestFlow.Infrastructure;

namespace HonestFlow;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\HonestFlow.SingleInstance.v3";
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
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
}
