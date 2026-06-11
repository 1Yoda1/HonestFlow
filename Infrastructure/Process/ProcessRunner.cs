using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HonestFlow.Infrastructure
{
    public static class ProcessRunner
    {
        public static int Run(string fileName, string arguments, bool asAdmin = false)
        {
            var result = RunDetailed(fileName, arguments, asAdmin).GetAwaiter().GetResult();
            return result.ExitCode;
        }

        public static async Task<int> RunAsync(string fileName, string arguments, bool asAdmin = false)
        {
            var result = await RunDetailed(fileName, arguments, asAdmin);
            return result.ExitCode;
        }

        public static async Task<ProcessExecutionResult> RunDetailed(string fileName, string arguments, bool asAdmin = false, int timeoutSeconds = 0)
        {
            var watch = Stopwatch.StartNew();
            var result = new ProcessExecutionResult();

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = asAdmin,
                    Verb = asAdmin ? "runas" : string.Empty,
                    CreateNoWindow = true,
                    RedirectStandardOutput = !asAdmin,
                    RedirectStandardError = !asAdmin
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                    throw new InvalidOperationException($"Не удалось запустить процесс: {fileName}");

                Task<string> outputTask = !asAdmin ? process.StandardOutput.ReadToEndAsync() : Task.FromResult(string.Empty);
                Task<string> errorTask = !asAdmin ? process.StandardError.ReadToEndAsync() : Task.FromResult(string.Empty);

                bool exited;
                if (timeoutSeconds > 0)
                    exited = await Task.Run(() => process.WaitForExit(timeoutSeconds * 1000));
                else
                {
                    await Task.Run(() => process.WaitForExit());
                    exited = true;
                }

                if (!exited)
                {
                    result.TimedOut = true;
                    try { process.Kill(true); } catch { }
                }

                result.StandardOutput = await outputTask;
                result.StandardError = await errorTask;
                result.ExitCode = exited ? process.ExitCode : -1;
            }
            catch (Exception ex)
            {
                result.Exception = ex;
                result.ExitCode = -1;
                Logger.LogException($"Запуск процесса: {fileName} {arguments}", ex);
            }
            finally
            {
                watch.Stop();
                result.Duration = watch.Elapsed;
                Logger.LogToFile($"Process: {fileName} {arguments} | ExitCode={result.ExitCode} | Duration={result.Duration.TotalSeconds:F1}s | TimedOut={result.TimedOut}", result.ExitCode != 0);
            }

            return result;
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
            var result = await RunDetailed(fileName, arguments, asAdmin);
            await Task.Delay(3000);
            return result.ExitCode;
        }
    }
}
