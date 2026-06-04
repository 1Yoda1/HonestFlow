using HonestFlow.Infrastructure.Services;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace HonestFlow.Infrastructure.Installers
{
    public class LmModuleInstaller
    {
        private readonly string _installerPath;

        public LmModuleInstaller(string installerPath)
        {
            _installerPath = installerPath;
        }

        public async Task ForceReinstall()
        {
            Logger.LogToFile("🗑️ Принудительная переустановка ЛМ ЧЗ...");

            string guid = FindLmModuleGuid();

            if (!string.IsNullOrEmpty(guid))
            {
                Logger.LogToFile($"Найден GUID: {guid}");
                await Uninstall(guid);
            }
            else
            {
                Logger.LogToFile("GUID не найден, возможно модуль не установлен");
            }

            await Install();

            Logger.LogToFile("✅ Переустановка завершена успешно");
        }

        private static async Task Uninstall(string guid)
        {
            Logger.LogToFile($"Удаление {guid}...");

            await WindowsServiceManager.StopService();
            await Task.Delay(1000);

            KillProcess("InstallAutoUpdateLM");
            KillProcess("Regime");

            var psi = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/x {guid} /qn /norestart",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true
            };

            using (var process = Process.Start(psi))
            {
                await Task.Run(() => process.WaitForExit());
                Logger.LogToFile($"msiexec /x exit code: {process.ExitCode}");
            }

            // Ждём с увеличивающимся интервалом (1, 2, 3, 5, 10 сек)
            await WaitForFullUninstall(guid, 30);
        }

        private async Task Install()
        {
            Logger.LogToFile($"📦 Установка {Path.GetFileName(_installerPath)}...");

            var psi = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/i \"{_installerPath}\" /qn /norestart",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            await Task.Run(() => process.WaitForExit());
            int exitCode = process.ExitCode;
            Logger.LogToFile($"msiexec install exit code: {exitCode}");

            if (exitCode != 0 && exitCode != 3010)
                throw new Exception($"Ошибка установки (код: {exitCode})");
        }

        /// <summary>
        /// Ожидание удаления с прогрессивным интервалом (меньше нагрузки)
        /// </summary>
        private static async Task WaitForFullUninstall(string guid, int timeoutSeconds = 30)
        {
            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);

            string[] registryPaths = new[]
            {
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{guid}",
                $@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{guid}"
            };

            int delayMs = 500;
            int maxDelayMs = 5000;

            while (DateTime.Now - startTime < timeout)
            {
                bool guidExists = false;

                foreach (var regPath in registryPaths)
                {
                    using var key = Registry.LocalMachine.OpenSubKey(regPath);
                    if (key != null)
                    {
                        guidExists = true;
                        break;
                    }
                }

                string serviceStatus = await WindowsServiceManager.GetServiceStatus();

                if (!guidExists && serviceStatus == "notfound")
                {
                    Logger.LogToFile("✅ Полное удаление подтверждено");
                    return;
                }

                // Увеличиваем интервал постепенно (меньше нагрузки на CPU)
                await Task.Delay(delayMs);
                delayMs = Math.Min(delayMs + 500, maxDelayMs);
            }

            Logger.LogToFile("⚠️ Таймаут ожидания полного удаления");
        }

        private static void KillProcess(string processName)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                foreach (var p in processes)
                {
                    Logger.LogToFile($"Завершаем {processName}.exe (PID: {p.Id})");
                    p.Kill();
                    p.WaitForExit(2000);
                }
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"Ошибка завершения процесса: {ex.Message}");
            }
        }

        private static string FindLmModuleGuid()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (key != null)
                    {
                        foreach (string subKeyName in key.GetSubKeyNames())
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            string displayName = subKey?.GetValue("DisplayName")?.ToString() ?? "";
                            if (displayName.Contains("Локальный модуль") || displayName.Contains("Regime"))
                            {
                                return subKeyName;
                            }
                        }
                    }
                }

                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (key != null)
                    {
                        foreach (string subKeyName in key.GetSubKeyNames())
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            string displayName = subKey?.GetValue("DisplayName")?.ToString() ?? "";
                            if (displayName.Contains("Локальный модуль") || displayName.Contains("Regime"))
                            {
                                return subKeyName;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"Ошибка поиска GUID: {ex.Message}");
            }

            return null;
        }

        public static string FindModulePath()
        {
            string[] paths =
            {
                @"C:\Program Files\Regime\bin\regime.cmd",
                @"C:\Program Files (x86)\Regime\bin\regime.cmd",
                @"F:\Program Files\Regime\bin\regime.cmd"
            };
            foreach (var p in paths)
                if (File.Exists(p)) return p;
            return null;
        }
    }
}