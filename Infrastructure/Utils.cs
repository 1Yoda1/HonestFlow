using System;
using System.Drawing;
using System.Windows.Forms;

namespace HonestFlow.Infrastructure
{
    public static class Utils
    {
        public static bool IsAdministrator()
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
    }
}
