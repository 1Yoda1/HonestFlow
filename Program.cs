using System;
using System.Windows.Forms;

namespace HonestFlow
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Настройка приложения (для .NET 6+)
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.Run(new MainForm());
        }
    }
}