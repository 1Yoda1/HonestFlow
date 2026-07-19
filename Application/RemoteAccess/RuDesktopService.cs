using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Core;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Models;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace HonestFlow.Application.RemoteAccess
{
    public class RuDesktopService
    {
        private const int CommandTimeoutSeconds = 15;
        private static readonly Regex IdRegex = new(@"\b\d{6,}\b", RegexOptions.Compiled);
        private static readonly Regex ExecutablePathRegex = new(
            "^(?:\"(?<path>[^\"]+\\.exe)\"|(?<path>.+?\\.exe))(?:\\s|$)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly ILogService _log;

        public RuDesktopService(ILogService log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public bool ShouldOfferPasswordSetup(IPData selectedIP)
        {
            if (selectedIP?.RuDesktop == null)
            {
                _log.LogDebug("RuDesktop: предложение настройки пропущено, нет блока RuDesktop в данных клиента");
                return false;
            }

            if (!selectedIP.RuDesktop.Enabled || !selectedIP.RuDesktop.AutoOfferPasswordSetup)
            {
                _log.LogDebug(
                    $"RuDesktop: предложение настройки пропущено, Enabled={selectedIP.RuDesktop.Enabled}, AutoOfferPasswordSetup={selectedIP.RuDesktop.AutoOfferPasswordSetup}");
                return false;
            }

            if (selectedIP.RuDesktop.SuppressPasswordSetupPrompt)
            {
                _log.LogDebug("RuDesktop: предложение настройки пропущено флагом клиента SuppressPasswordSetupPrompt");
                return false;
            }

            if (string.IsNullOrWhiteSpace(selectedIP.RuDesktop.Password))
            {
                _log.LogDebug("RuDesktop: предложение настройки пропущено, в карточке клиента не указан RuDesktop.Password");
                return false;
            }

            var state = LoadState();
            if (state.SuppressPasswordSetupPrompt)
            {
                _log.LogDebug("RuDesktop: предложение настройки пропущено локальным выбором пользователя");
                return false;
            }

            string passwordFingerprint = GetPasswordFingerprint(selectedIP.RuDesktop.Password);
            if (state.PasswordConfiguredByHonestFlow &&
                string.Equals(state.PasswordFingerprint, passwordFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                _log.LogDebug("RuDesktop: предложение настройки пропущено, пароль из карточки клиента уже применялся HonestFlow");
                return false;
            }

            return true;
        }

        public void SuppressPasswordSetupPrompt()
        {
            var state = LoadState();
            state.SuppressPasswordSetupPrompt = true;
            SaveState(state);
            _log.LogUser("RuDesktop: оператор отключил предложение настройки постоянного пароля");
        }

        public void SaveLastAuthorizedClient(IPData client)
        {
            if (client == null)
                return;

            var state = LoadState();
            state.LastAuthorizedClient = new LastAuthorizedClientState
            {
                Name = client.Name,
                Inn = client.Inn,
                AuthorizedAt = DateTime.Now
            };

            SaveState(state);
            _log.LogDebug($"RuDesktop: сохранен последний авторизованный клиент: {client.Name}");
        }

        public LastAuthorizedClientState GetLastAuthorizedClient()
        {
            return LoadState().LastAuthorizedClient;
        }

        public string GetLastKnownId()
        {
            return LoadState().LastKnownId;
        }

        public void ResetLocalConfigurationAfterInstallation()
        {
            var state = LoadState();
            ResetInstallationDependentState(state);
            SaveState(state);
            _log.LogDebug("RuDesktop: локальные признаки прежней установки сброшены после MSI");
        }

        public static void ResetInstallationDependentState(RuDesktopLocalState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            state.LastKnownId = null;
            state.PasswordConfiguredByHonestFlow = false;
            state.PasswordFingerprint = null;
            state.PasswordConfiguredAt = null;
            state.SuppressPasswordSetupPrompt = false;
        }

        public async Task<RuDesktopStatus> WaitForReady(
            TimeSpan timeout,
            TimeSpan pollInterval,
            CancellationToken cancellationToken = default)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            RuDesktopStatus lastStatus = null;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                lastStatus = await GetStatus();
                if (lastStatus.InstallationState == RuDesktopInstallationState.Ready &&
                    !string.IsNullOrWhiteSpace(lastStatus.Id))
                {
                    return lastStatus;
                }

                if (DateTime.UtcNow >= deadline)
                    break;

                await Task.Delay(pollInterval, cancellationToken);
            }
            while (true);

            return lastStatus ?? new RuDesktopStatus
            {
                InstallationState = RuDesktopInstallationState.CheckFailed,
                ErrorMessage = "Состояние RuDesktop не удалось получить."
            };
        }

        public async Task<bool> NeedsInitialPasswordSetup()
        {
            var status = await GetStatus();
            return status.IsInstalled && !status.PasswordConfiguredByHonestFlow;
        }

        public async Task<string> GetId()
        {
            string exePath = FindExecutablePath();
            if (string.IsNullOrWhiteSpace(exePath))
                return null;

            var result = await ProcessRunner.RunDetailed(exePath, "--get-id", timeoutSeconds: CommandTimeoutSeconds);
            if (!result.IsSuccess)
            {
                _log.LogDebug($"RuDesktop: не удалось получить ID. ExitCode={result.ExitCode}, Error={result.StandardError}");
                return null;
            }

            string id = ExtractId(result.StandardOutput);
            if (!string.IsNullOrWhiteSpace(id))
            {
                var state = LoadState();
                state.LastKnownId = id;
                SaveState(state);
            }

            return id;
        }

        public async Task<RuDesktopStatus> GetStatus()
        {
            var status = new RuDesktopStatus();

            try
            {
                string exePath = FindExecutablePath();
                status.IsInstalled = !string.IsNullOrWhiteSpace(exePath);

                var service = GetServiceStatus();
                if (!string.IsNullOrWhiteSpace(service.ErrorMessage))
                    throw new InvalidOperationException(service.ErrorMessage);

                status.ServiceInstalled = service.Installed;
                status.ServiceRunning = service.Running;
                status.InstallationState = ClassifyInstallationState(
                    status.IsInstalled,
                    status.ServiceInstalled,
                    status.ServiceRunning);

                if (status.IsInstalled)
                    status.Id = await GetId();

                var state = LoadState();
                if (status.InstallationState == RuDesktopInstallationState.NotInstalled &&
                    (!string.IsNullOrWhiteSpace(state.LastKnownId) ||
                     state.PasswordConfiguredByHonestFlow ||
                     !string.IsNullOrWhiteSpace(state.PasswordFingerprint) ||
                     state.PasswordConfiguredAt.HasValue ||
                     state.SuppressPasswordSetupPrompt))
                {
                    ResetInstallationDependentState(state);
                    SaveState(state);
                }

                status.PasswordConfiguredByHonestFlow = state.PasswordConfiguredByHonestFlow;
            }
            catch (Exception ex)
            {
                status.InstallationState = RuDesktopInstallationState.CheckFailed;
                status.ErrorMessage = ex.Message;
            }

            return status;
        }

        public static RuDesktopInstallationState ClassifyInstallationState(
            bool executableFound,
            bool serviceInstalled,
            bool serviceRunning)
        {
            if (!executableFound && !serviceInstalled)
                return RuDesktopInstallationState.NotInstalled;

            if (!executableFound || !serviceInstalled)
                return RuDesktopInstallationState.Damaged;

            return serviceRunning
                ? RuDesktopInstallationState.Ready
                : RuDesktopInstallationState.ServiceStopped;
        }

        public async Task<RuDesktopSetupResult> ConfigurePermanentPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return new RuDesktopSetupResult
                {
                    Success = false,
                    ErrorMessage = "В карточке клиента не указан постоянный пароль RuDesktop."
                };
            }

            string exePath = FindExecutablePath();
            if (string.IsNullOrWhiteSpace(exePath))
            {
                return new RuDesktopSetupResult
                {
                    Success = false,
                    ErrorMessage = "RuDesktop не найден на этом компьютере."
                };
            }

            string id = await GetId();
            if (string.IsNullOrWhiteSpace(id))
            {
                return new RuDesktopSetupResult
                {
                    Success = false,
                    ErrorMessage = "Не удалось получить ID RuDesktop."
                };
            }

            var result = await ProcessRunner.RunDetailed(
                exePath,
                $"--password {password}",
                timeoutSeconds: CommandTimeoutSeconds,
                logArguments: "--password ***");
            if (!result.IsSuccess)
            {
                _log.LogDebug($"RuDesktop: не удалось задать постоянный пароль. ExitCode={result.ExitCode}, Error={result.StandardError}");
                return new RuDesktopSetupResult
                {
                    Success = false,
                    Id = id,
                    ErrorMessage = "RuDesktop не принял команду установки постоянного пароля."
                };
            }

            var state = LoadState();
            state.LastKnownId = id;
            state.PasswordConfiguredByHonestFlow = true;
            state.PasswordFingerprint = GetPasswordFingerprint(password);
            state.PasswordConfiguredAt = DateTime.Now;
            SaveState(state);

            _log.LogUser($"RuDesktop: постоянный пароль создан, ID: {id}");

            return new RuDesktopSetupResult
            {
                Success = true,
                Id = id
            };
        }

        private static string ExtractId(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return null;

            return IdRegex.Match(output).Value;
        }

        private static string GetPasswordFingerprint(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                return null;

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hash);
        }

        private static string FindExecutablePath()
        {
            string servicePath = FindExecutablePathFromServiceRegistry();
            if (!string.IsNullOrWhiteSpace(servicePath))
                return servicePath;

            string registryPath = FindExecutablePathInRegistry();
            if (!string.IsNullOrWhiteSpace(registryPath))
                return registryPath;

            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RuDesktop", "rudesktop.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "RuDesktop", "rudesktop.exe")
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private static string FindExecutablePathInRegistry()
        {
            string[] roots =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (string root in roots)
            {
                using var key = Registry.LocalMachine.OpenSubKey(root);
                if (key == null)
                    continue;

                foreach (string subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    string displayName = subKey?.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName) ||
                        displayName.IndexOf("RuDesktop", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    string installLocation = subKey.GetValue("InstallLocation") as string;
                    if (string.IsNullOrWhiteSpace(installLocation))
                        continue;

                    string exePath = Path.Combine(installLocation, "rudesktop.exe");
                    if (File.Exists(exePath))
                        return exePath;
                }
            }

            return null;
        }

        private static string FindExecutablePathFromServiceRegistry()
        {
            using var serviceKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\RuDesktop");
            string imagePath = serviceKey?.GetValue("ImagePath") as string;
            string executablePath = TryExtractExecutablePath(imagePath);
            return !string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath)
                ? executablePath
                : null;
        }

        private static string TryExtractExecutablePath(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
                return null;

            string expanded = Environment.ExpandEnvironmentVariables(commandLine.Trim());
            Match match = ExecutablePathRegex.Match(expanded);
            return match.Success ? match.Groups["path"].Value.Trim() : null;
        }

        private static (bool Installed, bool Running, string ErrorMessage) GetServiceStatus()
        {
            try
            {
                var startInfo = new ProcessStartInfo("powershell.exe")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-ExecutionPolicy");
                startInfo.ArgumentList.Add("Bypass");
                startInfo.ArgumentList.Add("-Command");
                startInfo.ArgumentList.Add("Get-Service -Name RuDesktop -ErrorAction SilentlyContinue | ForEach-Object { [Console]::WriteLine($_.Status) }");

                using var process = Process.Start(startInfo);
                if (process == null)
                    return (false, false, "Не удалось запустить проверку службы RuDesktop.");

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(5000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2000);
                    return (false, false, "Проверка службы RuDesktop превысила допустимое время.");
                }

                string output = outputTask.GetAwaiter().GetResult().Trim();
                string error = errorTask.GetAwaiter().GetResult().Trim();

                if (process.ExitCode != 0)
                {
                    // Windows PowerShell returns exit code 1 with no output when
                    // Get-Service cannot find the requested service. That is a
                    // valid "not installed" result, not a probe failure.
                    if (string.IsNullOrWhiteSpace(output) && string.IsNullOrWhiteSpace(error))
                        return (false, false, null);

                    return (false, false, string.IsNullOrWhiteSpace(error)
                        ? "Не удалось проверить службу RuDesktop."
                        : error);
                }

                if (string.IsNullOrWhiteSpace(output))
                    return (false, false, null);

                return (true, string.Equals(output, "Running", StringComparison.OrdinalIgnoreCase), null);
            }
            catch (Exception ex)
            {
                return (false, false, $"Не удалось проверить службу RuDesktop: {ex.Message}");
            }
        }

        private static RuDesktopLocalState LoadState()
        {
            try
            {
                if (!File.Exists(AppPaths.RuDesktopStateFile))
                    return new RuDesktopLocalState();

                string json = File.ReadAllText(AppPaths.RuDesktopStateFile);
                return JsonConvert.DeserializeObject<RuDesktopLocalState>(json) ?? new RuDesktopLocalState();
            }
            catch
            {
                return new RuDesktopLocalState();
            }
        }

        private static void SaveState(RuDesktopLocalState state)
        {
            Directory.CreateDirectory(AppPaths.ProgramDataFolder);
            string json = JsonConvert.SerializeObject(state ?? new RuDesktopLocalState(), Formatting.Indented);
            File.WriteAllText(AppPaths.RuDesktopStateFile, json);
        }
    }
}
