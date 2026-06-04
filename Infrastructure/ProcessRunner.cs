using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HonestFlow.Infrastructure
{
    public static class ProcessRunner
    {
        public static int Run(string fileName, string arguments, bool asAdmin = false)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = asAdmin ? "runas" : "",
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            process.WaitForExit();
            return process.ExitCode;
        }

        public static async Task<int> RunAsync(string fileName, string arguments, bool asAdmin = false)
        {
            return await Task.Run(() => Run(fileName, arguments, asAdmin));
        }

        public static void OpenUninstallPrograms()
        {
            try
            {
                Process.Start("control", "appwiz.cpl");
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"Не удалось открыть окно программ: {ex.Message}", true);
            }
        }
        public static async Task<int> RunWithChildTracking(string fileName, string arguments, bool asAdmin = false)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = asAdmin ? "runas" : "",
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            
            await Task.Run(() => process.WaitForExit()); // Ждём основной процесс

            await Task.Delay(3000); // Небольшая пауза для дочерних процессов

            return process.ExitCode;
        }
    }
}