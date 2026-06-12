using System;
using System.Drawing;
using System.Windows.Forms;

namespace HonestFlow.Infrastructure
{
    public static class Utils
    {
        public static RichTextBox LogBox { get; set; }

        //private static void WriteToUi(string message, Color color)
        //{
        //    if (LogBox == null) return;

        //    if (LogBox.InvokeRequired)
        //    {
        //        LogBox.Invoke(new Action(() => WriteToUi(message, color)));
        //        return;
        //    }

        //    string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        //    string formatted = $"[{timestamp}] {message}";
        //    LogBox.AppendText(formatted + Environment.NewLine);

        //    int start = Math.Max(0, LogBox.TextLength - formatted.Length - Environment.NewLine.Length);
        //    LogBox.Select(start, formatted.Length);
        //    LogBox.SelectionColor = color;
        //    LogBox.Select(LogBox.TextLength, 0);
        //    LogBox.ScrollToCaret();
        //}

        public static bool IsAdministrator()
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
    }
}
