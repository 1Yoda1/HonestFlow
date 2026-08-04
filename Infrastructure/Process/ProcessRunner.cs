using System;
using System.Diagnostics;
using System.Threading;
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

        public static async Task<int> RunAsync(
            string fileName,
            string arguments,
            bool asAdmin = false,
            CancellationToken cancellationToken = default)
        {
            var result = await RunDetailed(fileName, arguments, asAdmin, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result.ExitCode;
        }

        public static async Task<ProcessExecutionResult> RunDetailed(
            string fileName,
            string arguments,
            bool asAdmin = false,
            int timeoutSeconds = 0,
            string logArguments = null,
            CancellationToken cancellationToken = default)
        {
            var watch = Stopwatch.StartNew();
            var result = new ProcessExecutionResult();
            string safeArguments = logArguments ?? arguments;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
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

                bool exited = true;
                if (timeoutSeconds > 0)
                {
                    using var timeout = new CancellationTokenSource(
                        TimeSpan.FromSeconds(timeoutSeconds));
                    using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        timeout.Token,
                        cancellationToken);
                    try
                    {
                        await process.WaitForExitAsync(linkedCancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        exited = process.HasExited;
                        if (cancellationToken.IsCancellationRequested)
                        {
                            if (!exited)
                                await process.WaitForExitAsync().ConfigureAwait(false);
                            throw new OperationCanceledException(cancellationToken);
                        }

                        if (!exited)
                        {
                            result.TimedOut = true;
                            await TerminateProcessAsync(process).ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    await process.WaitForExitAsync().ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                result.StandardOutput = await CompleteReadTask(outputTask, TimeSpan.FromSeconds(5));
                result.StandardError = await CompleteReadTask(errorTask, TimeSpan.FromSeconds(5));
                result.ExitCode = exited ? process.ExitCode : -1;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                result.Exception = new OperationCanceledException(cancellationToken);
                result.ExitCode = -1;
                throw;
            }
            catch (Exception ex)
            {
                result.Exception = ex;
                result.ExitCode = -1;
                Logger.LogException($"Запуск процесса: {fileName} {safeArguments}", ex);
            }
            finally
            {
                watch.Stop();
                result.Duration = watch.Elapsed;
                Logger.LogToFile($"Process: {fileName} {safeArguments} | ExitCode={result.ExitCode} | Duration={result.Duration.TotalSeconds:F1}s | TimedOut={result.TimedOut}", result.ExitCode != 0);
            }

            return result;
        }

        private static async Task<string> CompleteReadTask(Task<string> readTask, TimeSpan timeout)
        {
            try
            {
                return await readTask.WaitAsync(timeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return string.Empty;
            }
        }

        private static async Task TerminateProcessAsync(Process process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                return;
            }

            using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(shutdownTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
