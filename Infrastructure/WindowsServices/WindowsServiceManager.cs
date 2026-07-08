using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HonestFlow.Infrastructure.WindowsServices
{
    /// <summary>
    /// Управление Windows-службой Regime (Локальный модуль ЧЗ).
    /// Публичного удаления службы здесь нет: удаление должно выполняться MSI uninstall-сценарием.
    /// </summary>
    public static class WindowsServiceManager
    {
        public static async Task StopService()
        {
            try
            {
                await ProcessRunner.RunAsync("net", "stop Regime", true);
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"Ошибка при остановке службы Regime: {ex.Message}", true);
            }
        }

        public static async Task StartService()
        {
            await StartService("Regime");
        }

        public static async Task StartLmServices()
        {
            await StartService("yenisei");
            await StartService("Regime");
        }

        public static async Task StartService(string serviceName)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                return;

            try
            {
                int exitCode = await ProcessRunner.RunAsync("net", $"start {serviceName}", true);
                if (exitCode != 0 && exitCode != 2)
                    Logger.LogToFile($"Ошибка при запуске службы {serviceName}: net start вернул код {exitCode}", true);
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"Ошибка при запуске службы {serviceName}: {ex.Message}", true);
            }
        }

        public static async Task<string> GetServiceStatus()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sc",
                    Arguments = "query Regime",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return "notfound";

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                string text = output + Environment.NewLine + error;

                if (process.ExitCode != 0 && IsServiceNotFoundResponse(text))
                    return "notfound";

                if (text.Contains("RUNNING"))
                    return "running";

                if (text.Contains("START_PENDING"))
                    return "startpending";

                if (text.Contains("STOPPED"))
                    return "stopped";

                return "unknown";
            }
            catch
            {
                return "notfound";
            }
        }

        private static bool IsServiceNotFoundResponse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.Contains("1060", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("не существует", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("не установлена", StringComparison.OrdinalIgnoreCase);
        }
    }
}
