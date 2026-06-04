using Microsoft.Win32;

namespace HonestFlow.Infrastructure.Services
{
    /// <summary>
    /// Работа с реестром Windows (поиск установленных программ)
    /// </summary>
    public static class RegistryManager
    {
        /// <summary>
        /// Проверяет, установлен ли Локальный модуль ЧЗ (Regime)
        /// </summary>
        public static bool IsLmModuleInstalled()
        {
            string[] paths =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var regPath in paths)
            {
                using var key = Registry.LocalMachine.OpenSubKey(regPath);
                if (key == null) continue;
                foreach (string sub in key.GetSubKeyNames())
                {
                    using var subkey = key.OpenSubKey(sub);
                    string name = subkey?.GetValue("DisplayName")?.ToString();
                    if (name?.Contains("Локальный модуль ЧЗ") == true)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Получает GUID установленного Локального модуля ЧЗ для удаления
        /// </summary>
        public static string GetLmModuleGuid()
        {
            string[] paths =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var regPath in paths)
            {
                using var key = Registry.LocalMachine.OpenSubKey(regPath);
                if (key == null) continue;
                foreach (string sub in key.GetSubKeyNames())
                {
                    using var subkey = key.OpenSubKey(sub);
                    string name = subkey?.GetValue("DisplayName")?.ToString();
                    if (name?.Contains("Локальный модуль ЧЗ") == true)
                    {
                        string uninst = subkey?.GetValue("UninstallString")?.ToString();
                        if (uninst?.Contains('{') == true)
                        {
                            int s = uninst.IndexOf('{'), e = uninst.IndexOf('}');
                            return uninst.Substring(s, e - s + 1);
                        }
                    }
                }
            }
            return null;
        }
    }
}