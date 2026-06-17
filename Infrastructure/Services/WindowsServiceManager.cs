using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HonestFlow.Infrastructure.Services
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
            try
            {
                await ProcessRunner.RunAsync("net", "start Regime", true);
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"Ошибка при запуске службы Regime: {ex.Message}", true);
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
                    RedirectStandardOutput = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return "notfound";

                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (output.Contains("RUNNING"))
                    return "running";

                if (output.Contains("START_PENDING"))
                    return "startpending";

                if (output.Contains("STOPPED"))
                    return "stopped";

                return "unknown";
            }
            catch
            {
                return "notfound";
            }
        }
    }
}
