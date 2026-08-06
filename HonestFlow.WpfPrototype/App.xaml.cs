using System.Windows;
using HonestFlow.Infrastructure;

namespace HonestFlow.WpfPrototype;

public partial class App : System.Windows.Application
{
    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Info("WPF application closed", nameof(App));
        Logger.Shutdown();
        base.OnExit(e);
    }
}
