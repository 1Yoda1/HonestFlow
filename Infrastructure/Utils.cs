using System;
using System.Drawing;
using System.Windows.Forms;

namespace HonestFlow.Infrastructure
{
    public static class Utils
    {
        public static RichTextBox LogBox { get; set; }

        public static void Log(string message, bool isError = false)
        {
            if (LogBox == null) return;
            if (LogBox.InvokeRequired)
            {
                LogBox.Invoke(new Action(() => Log(message, isError)));
                return;
            }

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string formatted = $"[{timestamp}] {message}";
            LogBox.AppendText(formatted + Environment.NewLine);

            int start = LogBox.TextLength - formatted.Length - 2;
            LogBox.Select(start, formatted.Length);
            LogBox.SelectionColor = isError ? Color.Red : Color.LightGreen;
            LogBox.Select(LogBox.TextLength, 0);
            LogBox.ScrollToCaret();
            Application.DoEvents();
            Logger.LogToFile(message, isError);
        }

        public static bool IsAdministrator()
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
    }
}