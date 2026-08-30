using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Installation;

namespace HonestFlow.Infrastructure
{
    public sealed class KktBootstrapProcessClient : IKktBootstrapProcessClient
    {
        private readonly KktBootstrapResourceExtractor _extractor;

        public KktBootstrapProcessClient(KktBootstrapResourceExtractor extractor = null)
        {
            _extractor = extractor ?? new KktBootstrapResourceExtractor();
        }

        public async Task<KktBootstrapStartResult> StartAsync(
            string architecture,
            string requiredDriverVersion,
            CancellationToken cancellationToken)
        {
            string helperPath = _extractor.Extract(architecture);
            var startInfo = new ProcessStartInfo
            {
                FileName = helperPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--arch");
            startInfo.ArgumentList.Add(architecture);
            startInfo.ArgumentList.Add("--version");
            startInfo.ArgumentList.Add(requiredDriverVersion);

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!process.Start())
            {
                process.Dispose();
                return KktBootstrapStartResult.Error(KktBootstrapStartStatus.Failed, "HELPER_START_FAILED",
                    "Не удалось запустить модуль подключения к ККТ.");
            }

            Task<string> stderr = process.StandardError.ReadToEndAsync();
            try
            {
                while (!process.HasExited)
                {
                    string line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line == null) break;
                    KktBootstrapProtocolEvent message = KktBootstrapProtocolEvent.Parse(line);
                    if (string.Equals(message.Name, "CONNECTED", StringComparison.OrdinalIgnoreCase))
                    {
                        return KktBootstrapStartResult.Connected(
                            new KktBootstrapProcessSession(process, stderr),
                            message.ToConnectionInfo());
                    }
                    if (string.Equals(message.Name, "ERROR", StringComparison.OrdinalIgnoreCase))
                    {
                        await WaitForExitBoundedAsync(process).ConfigureAwait(false);
                        process.Dispose();
                        return message.ToStartFailure();
                    }
                }

                string error = await stderr.ConfigureAwait(false);
                process.Dispose();
                return KktBootstrapStartResult.Error(KktBootstrapStartStatus.Failed, "HELPER_EXITED",
                    string.IsNullOrWhiteSpace(error) ? "Модуль подключения к ККТ завершился до установления соединения." : error.Trim());
            }
            catch
            {
                await TerminateBoundedAsync(process).ConfigureAwait(false);
                process.Dispose();
                throw;
            }
        }

        internal static async Task WaitForExitBoundedAsync(Process process)
        {
            if (process.HasExited) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { await TerminateBoundedAsync(process).ConfigureAwait(false); }
        }

        internal static Task TerminateBoundedAsync(Process process)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            return Task.CompletedTask;
        }

        private sealed class KktBootstrapProcessSession : IKktBootstrapSession
        {
            private readonly Process _process;
            private readonly Task<string> _stderr;
            private bool _closed;

            public KktBootstrapProcessSession(Process process, Task<string> stderr)
            {
                _process = process;
                _stderr = stderr;
            }

            public bool IsRunning => !_closed && !_process.HasExited;

            public async Task CloseAsync(CancellationToken cancellationToken)
            {
                if (_closed) return;
                _closed = true;
                try
                {
                    if (!_process.HasExited)
                    {
                        await _process.StandardInput.WriteLineAsync("CLOSE").ConfigureAwait(false);
                        await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        timeout.CancelAfter(TimeSpan.FromSeconds(5));
                        while (!_process.HasExited)
                        {
                            string line = await _process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                            if (line == null || string.Equals(
                                    KktBootstrapProtocolEvent.Parse(line).Name,
                                    "CLOSED",
                                    StringComparison.OrdinalIgnoreCase))
                                break;
                        }
                        await WaitForExitBoundedAsync(_process).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException)
                {
                    await TerminateBoundedAsync(_process).ConfigureAwait(false);
                }
                _ = await _stderr.ConfigureAwait(false);
            }

            public async ValueTask DisposeAsync()
            {
                if (!_closed) await CloseAsync(CancellationToken.None).ConfigureAwait(false);
                _process.Dispose();
            }
        }
    }

    public sealed class KktBootstrapResourceExtractor
    {
        private const string ResourcePrefix = "HonestFlow.KktBootstrap.";
        private readonly Assembly _assembly;
        private readonly string _runtimeDirectory;

        public KktBootstrapResourceExtractor(Assembly assembly = null, string runtimeDirectory = null)
        {
            _assembly = assembly ?? typeof(KktBootstrapResourceExtractor).Assembly;
            _runtimeDirectory = runtimeDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HonestFlow", "Runtime", "KktBootstrap");
        }

        public string Extract(string architecture)
        {
            string normalized = string.Equals(architecture, "x86", StringComparison.OrdinalIgnoreCase) ? "x86" :
                string.Equals(architecture, "x64", StringComparison.OrdinalIgnoreCase) ? "x64" :
                throw new ArgumentException("Поддерживаются только x86 и x64.", nameof(architecture));
            string resourceName = ResourcePrefix + normalized + ".exe";
            using Stream resource = _assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException("В сборке отсутствует " + resourceName + ".");
            using var memory = new MemoryStream();
            resource.CopyTo(memory);
            byte[] bytes = memory.ToArray();
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).Substring(0, 16).ToLowerInvariant();
            Directory.CreateDirectory(_runtimeDirectory);
            string path = Path.Combine(_runtimeDirectory, $"KktBootstrap.{normalized}.{hash}.exe");
            bool current = File.Exists(path) &&
                           SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(SHA256.HashData(bytes));
            if (!current)
                File.WriteAllBytes(path, bytes);
            return path;
        }
    }
}
