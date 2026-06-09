using System;
using System.Windows.Forms;
using HonestFlow.Infrastructure;

namespace HonestFlow
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Logger.Initialize();
            // Настройка приложения (для .NET 6+)
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.Run(new MainForm());
        }
    }
}