using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Infrastructure;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class ProcessRunnerTests
    {
        [Fact]
        public async Task RunDetailed_CapturesStandardOutputAndError()
        {
            ProcessExecutionResult result = await ProcessRunner.RunDetailed(
                GetCommandInterpreter(),
                "/d /s /c \"echo stdout-value & echo stderr-value 1>&2 & exit /b 0\"",
                timeoutSeconds: 5);

            Assert.True(result.IsSuccess);
            Assert.Contains("stdout-value", result.StandardOutput);
            Assert.Contains("stderr-value", result.StandardError);
            Assert.False(result.TimedOut);
            Assert.Null(result.Exception);
        }

        [Fact]
        public async Task RunDetailed_PreservesNonZeroExitCode()
        {
            ProcessExecutionResult result = await ProcessRunner.RunDetailed(
                GetCommandInterpreter(),
                "/d /s /c \"exit /b 7\"",
                timeoutSeconds: 5);

            Assert.Equal(7, result.ExitCode);
            Assert.False(result.IsSuccess);
            Assert.False(result.TimedOut);
            Assert.Null(result.Exception);
        }

        [Fact]
        public async Task RunDetailed_TerminatesProcessTreeOnTimeout()
        {
            string windowsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string powershell = Path.Combine(
                windowsFolder,
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");

            ProcessExecutionResult result = await ProcessRunner.RunDetailed(
                powershell,
                "-NoProfile -Command \"Start-Sleep -Seconds 10\"",
                timeoutSeconds: 1);

            Assert.True(result.TimedOut);
            Assert.Equal(-1, result.ExitCode);
            Assert.False(result.IsSuccess);
            Assert.True(result.Duration < TimeSpan.FromSeconds(7));
            Assert.Null(result.Exception);
        }

        [Fact]
        public async Task RunDetailed_CancellationWaitsForCurrentProcessAndThrows()
        {
            string windowsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string powershell = Path.Combine(
                windowsFolder,
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            var started = DateTime.UtcNow;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                ProcessRunner.RunDetailed(
                    powershell,
                    "-NoProfile -Command \"Start-Sleep -Seconds 1\"",
                    cancellationToken: cancellation.Token));

            TimeSpan duration = DateTime.UtcNow - started;
            Assert.True(duration >= TimeSpan.FromMilliseconds(700));
            Assert.True(duration < TimeSpan.FromSeconds(7));
        }

        private static string GetCommandInterpreter() =>
            Environment.GetEnvironmentVariable("ComSpec") ??
            Path.Combine(Environment.SystemDirectory, "cmd.exe");
    }
}
