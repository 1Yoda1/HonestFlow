using System;
using System.Windows.Forms;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Updates;

namespace HonestFlow
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                Logger.Initialize();
                Logger.Info("Запуск приложения", nameof(Program));

                var updater = new SelfUpdateService();
                bool updateStarted = updater.CheckDownloadAndRunUpdateIfNeeded()
                    .GetAwaiter()
                    .GetResult();

                if (updateStarted)
                    return;

                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                System.Windows.Forms.Application.Run(new MainForm());

                Logger.Info("Приложение закрыто", nameof(Program));
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Критическая ошибка при запуске приложения", nameof(Program));

                MessageBox.Show(
                    $"Критическая ошибка:\n{ex.Message}\n\nЛог: {Logger.GetLogPath()}",
                    "HonestFlow",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}