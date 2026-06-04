using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HonestFlow.Infrastructure.Services
{
    /// <summary>
    /// Управление Windows-службой Regime (Локальный модуль ЧЗ)
    /// </summary>
    public static class WindowsServiceManager
    {
        /// <summary>
        /// Остановка службы Regime
        /// </summary>
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

        /// <summary>
        /// Запуск службы Regime
        /// </summary>
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

        /// <summary>
        /// Удаление службы Regime из системы
        /// </summary>
        public static async Task DeleteService()
        {
            try
            {
                await ProcessRunner.RunAsync("sc", "delete Regime", true);
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"Ошибка при удалении службы Regime: {ex.Message}", true);
            }
        }

        /// <summary>
        /// Получить статус службы Regime (running, stopped, notfound)
        /// </summary>
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
                string output = await process.StandardOutput.ReadToEndAsync();
                if (output.Contains("RUNNING")) return "running";
                if (output.Contains("STOPPED")) return "stopped";
                return "unknown";
            }
            catch
            {
                return "notfound";
            }
        }
    }
}