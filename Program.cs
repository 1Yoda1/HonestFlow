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
            {
                Logger.Initialize();
                // Настройка приложения (для .NET 6+)
                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                System.Windows.Forms.Application.Run(new MainForm());
            }
        }
    }
}