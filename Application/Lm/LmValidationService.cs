using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Infrastructure.WindowsServices;
using HonestFlow.Models;
using HonestFlow.Application.Core;
using Microsoft.Win32;

namespace HonestFlow.Application.Lm
{
    /// <summary>
    /// Легкая проверка ЛМ ЧЗ для построения плана установки.
    /// Установленность определяется только физическими признаками, API отвечает только за runtime-состояние.
    /// </summary>
    public class LmValidationService : ILmValidationService
    {
        private const int LmApiPort = 5995;
        private const string RuntimeUnavailable = "unavailable";

        private readonly ILogService _log;
        private LmModuleService _cachedLmModule;
        private string _cachedExpectedVersion;
        private readonly object _lockObject = new();

        public LmValidationService(ILogService logService)
        {
            _log = logService;
        }

        private LmModuleService GetLmModule(string expectedVersion)
        {
            if (string.IsNullOrWhiteSpace(expectedVersion))
                throw new InvalidOperationException("Версия ЛМ ЧЗ не задана. Проверьте versions.json / конфигурацию источника установщиков.");

            lock (_lockObject)
            {
                if (_cachedLmModule == null || _cachedExpectedVersion != expectedVersion)
                {
                    _cachedLmModule = new LmModuleService(string.Empty, expectedVersion, _log);
                    _cachedExpectedVersion = expectedVersion;
                    _log.LogDebug($"LmModuleService создан и закэширован для версии {expectedVersion}");
                }

                return _cachedLmModule;
            }
        }

        public async Task<LmStatus> GetLmStatus(string expectedVersion)
        {
            var lmModule = GetLmModule(expectedVersion);
            return await lmModule.GetStatus();
        }

        public async Task<LmValidationResult> CheckLmStatus(string expectedVersion)
        {
            if (string.IsNullOrWhiteSpace(expectedVersion))
            {
                _log.LogDebug("ЛМ ЧЗ: версия не задана, проверка невозможна.");
                return new LmValidationResult
                {
                    IsPhysicallyInstalled = false,
                    RuntimeStatus = RuntimeUnavailable,
                    DiagnosticStatus = "ConfigurationError",
                    NeedsInstall = true,
                    DisplayStatus = "ошибка: версия ЛМ не задана",
                    DecisionReason = "Не задана ожидаемая версия ЛМ ЧЗ"
                };
            }

            var physical = GetPhysicalState();
            LogPhysicalState(physical);

            var result = new LmValidationResult
            {
                IsPhysicallyInstalled = physical.IsInstalled,
                PhysicalVersion = physical.Version,
                RuntimeStatus = RuntimeUnavailable,
                DiagnosticStatus = physical.IsInstalled ? "API not checked" : "PhysicallyAbsent"
            };

            if (!physical.IsInstalled)
            {
                result.NeedsInstall = true;
                result.DisplayStatus = "не установлен";
                result.DecisionReason = "Физические признаки ЛМ ЧЗ отсутствуют";
                LogDecision(result, expectedVersion);
                return result;
            }

            using var apiClient = new LmApiClient(true);
            var apiResponse = await apiClient.GetStatus();
            if (apiResponse.IsSuccess && apiResponse.Data != null)
            {
                ApplyApiStatus(result, apiResponse.Data);
                ApplyDecision(result, expectedVersion);
                LogDecision(result, expectedVersion);
                return result;
            }

            _log.LogDebug($"ЛМ ЧЗ: API не ответил ({apiResponse.ErrorMessage}). Запускаем дополнительную диагностику без смены Installed=false.");
            await DiagnoseUnavailableApi(result, expectedVersion);
            ApplyDecision(result, expectedVersion);
            LogDecision(result, expectedVersion);
            return result;
        }

        public async Task<(bool NeedInstall, string DisplayStatus)> GetLmStatusInfo(string expectedVersion)
        {
            var result = await CheckLmStatus(expectedVersion);
            return (result.NeedsInstall, result.DisplayStatus);
        }

        private static void ApplyApiStatus(LmValidationResult result, LmStatus status)
        {
            result.ApiStatus = status;
            result.RuntimeStatus = string.IsNullOrWhiteSpace(status.Status) ? RuntimeUnavailable : status.Status;
            result.DiagnosticStatus = "API OK";
        }

        private async Task DiagnoseUnavailableApi(LmValidationResult result, string expectedVersion)
        {
            var samples = await SampleRegimeServiceStates();
            if (IsServiceUnstable(samples))
            {
                result.DiagnosticStatus = "ServiceUnstable";
                _log.LogDebug($"ЛМ ЧЗ: служба Regime нестабильна, последовательность состояний: {string.Join(" -> ", samples)}");
                return;
            }

            string serviceStatus = samples.LastOrDefault() ?? await WindowsServiceManager.GetServiceStatus();
            _log.LogDebug($"ЛМ ЧЗ: состояние службы Regime после timeout API: {serviceStatus}");

            if (IsStopped(serviceStatus))
            {
                _log.LogDebug("ЛМ ЧЗ: служба Regime остановлена, пробуем запустить и повторить запрос API.");
                await WindowsServiceManager.StartService();

                var retryStatus = await WaitForApiStatusAfterServiceStart();
                if (retryStatus != null)
                {
                    ApplyApiStatus(result, retryStatus);
                    return;
                }
            }

            bool portOpen = await IsPortOpen("127.0.0.1", LmApiPort, 1500);
            result.DiagnosticStatus = portOpen ? "API unavailable" : "Port closed";
            _log.LogDebug(portOpen
                ? $"ЛМ ЧЗ: порт {LmApiPort} слушается, но API статуса не отвечает."
                : $"ЛМ ЧЗ: порт {LmApiPort} не слушается.");
        }

        private async Task<LmStatus> WaitForApiStatusAfterServiceStart()
        {
            const int maxAttempts = 8;
            const int initialDelayMs = 1000;
            const int maxDelayMs = 4000;

            int delayMs = initialDelayMs;
            string lastError = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (attempt > 1)
                    await Task.Delay(delayMs);

                using var retryClient = new LmApiClient(true);
                var retryResponse = await retryClient.GetStatus();
                if (retryResponse.IsSuccess && retryResponse.Data != null)
                {
                    _log.LogDebug($"ЛМ ЧЗ: API ответил после запуска службы Regime, попытка {attempt}/{maxAttempts}.");
                    return retryResponse.Data;
                }

                lastError = retryResponse.ErrorMessage;
                _log.LogDebug($"ЛМ ЧЗ: API еще не ответил после запуска Regime, попытка {attempt}/{maxAttempts}: {lastError}");
                delayMs = Math.Min(delayMs * 2, maxDelayMs);
            }

            _log.LogDebug($"ЛМ ЧЗ: API не ответил после запуска Regime за {maxAttempts} попыток. Последняя ошибка: {lastError}");
            return null;
        }

        private void ApplyDecision(LmValidationResult result, string expectedVersion)
        {
            string version = GetBestKnownVersion(result);
            bool versionKnown = !string.IsNullOrWhiteSpace(version);
            bool versionMismatch = versionKnown && version != expectedVersion;
            bool unavailableWithUnknownVersion =
                result.IsPhysicallyInstalled &&
                !versionKnown &&
                result.RuntimeStatus == RuntimeUnavailable;

            result.NeedsInstall = !result.IsPhysicallyInstalled || versionMismatch || unavailableWithUnknownVersion;
            result.NeedsInitialize = !result.NeedsInstall && result.RuntimeStatus == "not_configured";

            if (!result.IsPhysicallyInstalled)
            {
                result.DisplayStatus = "не установлен";
                result.DecisionReason = "NeedsInstall=true: физические признаки ЛМ ЧЗ отсутствуют";
            }
            else if (versionMismatch)
            {
                result.DisplayStatus = $"версия {version}, требуется {expectedVersion}";
                result.DecisionReason = "NeedsInstall=true: версия ЛМ ЧЗ не совпадает с ожидаемой";
            }
            else if (unavailableWithUnknownVersion)
            {
                result.DisplayStatus = $"{result.DiagnosticStatus}; версия не определена";
                result.DecisionReason = "NeedsInstall=true: ЛМ найден физически, но API недоступен и версия не определена";
            }
            else if (result.NeedsInitialize)
            {
                result.DisplayStatus = "не инициализирован";
                result.DecisionReason = "NeedsInitialize=true: runtime status API = not_configured";
            }
            else if (result.RuntimeStatus == "ready")
            {
                result.DisplayStatus = $"OK, версия {version ?? "не определена"}";
                result.DecisionReason = "NeedsInstall=false: ЛМ установлен физически, версия совпадает, API ready";
            }
            else if (result.RuntimeStatus == "initialization")
            {
                result.DisplayStatus = $"OK (инициализирован), версия {version ?? "не определена"}";
                result.DecisionReason = "NeedsInstall=false: ЛМ установлен физически, версия совпадает, API initialization";
            }
            else
            {
                result.DisplayStatus = $"{result.DiagnosticStatus}; версия {version ?? "не определена"}";
                result.DecisionReason = $"NeedsInstall=false: ЛМ установлен физически; недоступность API ({result.DiagnosticStatus}) является предупреждением";
            }
        }

        private static string GetBestKnownVersion(LmValidationResult result)
        {
            return !string.IsNullOrWhiteSpace(result.ApiStatus?.Version)
                ? result.ApiStatus.Version
                : result.PhysicalVersion;
        }

        private void LogDecision(LmValidationResult result, string expectedVersion)
        {
            _log.LogDebug(
                "Решение по ЛМ ЧЗ: " +
                $"Installed={result.IsPhysicallyInstalled}; " +
                $"PhysicalVersion={result.PhysicalVersion ?? "unknown"}; " +
                $"ApiVersion={result.ApiStatus?.Version ?? "unknown"}; " +
                $"ExpectedVersion={expectedVersion}; " +
                $"RuntimeStatus={result.RuntimeStatus}; " +
                $"DiagnosticStatus={result.DiagnosticStatus}; " +
                $"NeedsInstall={result.NeedsInstall}; " +
                $"NeedsInitialize={result.NeedsInitialize}; " +
                $"Reason={result.DecisionReason}");
        }

        private void LogPhysicalState(LmPhysicalState state)
        {
            _log.LogDebug(
                "Физические признаки ЛМ ЧЗ: " +
                $"RegimeService={state.RegimeServiceExists}; " +
                $"YeniseiService={state.YeniseiServiceExists}; " +
                $"UninstallRegistry={state.UninstallRegistryExists}; " +
                $"InstallLocation={state.InstallLocation ?? "none"}; " +
                $"InstallFolderExists={state.InstallFolderExists}; " +
                $"Version={state.Version ?? "unknown"}; " +
                $"Installed={state.IsInstalled}");
        }

        private static async Task<List<string>> SampleRegimeServiceStates()
        {
            var samples = new List<string>();

            for (int i = 0; i < 8; i++)
            {
                samples.Add(await WindowsServiceManager.GetServiceStatus());
                await Task.Delay(500);
            }

            return samples;
        }

        private static bool IsServiceUnstable(List<string> samples)
        {
            var compact = samples
                .Where(s => s == "startpending" || s == "running")
                .Aggregate(new List<string>(), (acc, value) =>
                {
                    if (acc.Count == 0 || acc[acc.Count - 1] != value)
                        acc.Add(value);
                    return acc;
                });

            return compact.Count >= 4 &&
                   compact.Contains("startpending") &&
                   compact.Contains("running");
        }

        private static bool IsStopped(string status)
        {
            return string.Equals(status, "stopped", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<bool> IsPortOpen(string host, int port, int timeoutMs)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(host, port);
                var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));

                return completed == connectTask && client.Connected;
            }
            catch
            {
                return false;
            }
        }

        private static LmPhysicalState GetPhysicalState()
        {
            var state = new LmPhysicalState
            {
                RegimeServiceExists = ServiceKeyExists("Regime"),
                YeniseiServiceExists = ServiceKeyExists("yenisei")
            };

            foreach (var registryInfo in GetUninstallRegistryInfo())
            {
                state.UninstallRegistryExists = true;

                if (string.IsNullOrWhiteSpace(state.Version))
                    state.Version = registryInfo.DisplayVersion;

                if (string.IsNullOrWhiteSpace(state.InstallLocation))
                    state.InstallLocation = registryInfo.InstallLocation;
            }

            var folders = GetInstallFolderCandidates(state.InstallLocation).ToArray();
            state.InstallFolderExists = folders.Any(Directory.Exists);
            state.IsInstalled =
                state.RegimeServiceExists ||
                state.YeniseiServiceExists ||
                state.UninstallRegistryExists ||
                state.InstallFolderExists;

            return state;
        }

        private static bool ServiceKeyExists(string serviceName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<string> GetInstallFolderCandidates(string installLocation)
        {
            if (!string.IsNullOrWhiteSpace(installLocation))
                yield return installLocation;

            yield return @"F:\Program Files\Regime";
            yield return @"C:\Program Files\Regime";
            yield return @"C:\Program Files (x86)\Regime";
        }

        private static IEnumerable<LmRegistryInfo> GetUninstallRegistryInfo()
        {
            string[] uninstallRoots =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (string root in uninstallRoots)
            {
                using var key = Registry.LocalMachine.OpenSubKey(root);
                if (key == null)
                    continue;

                foreach (string subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    string displayName = subKey?.GetValue("DisplayName")?.ToString() ?? string.Empty;

                    if (!displayName.Contains("Regime", StringComparison.OrdinalIgnoreCase))
                        continue;

                    yield return new LmRegistryInfo
                    {
                        DisplayVersion = subKey.GetValue("DisplayVersion")?.ToString(),
                        InstallLocation = subKey.GetValue("InstallLocation")?.ToString()
                    };
                }
            }
        }

        private class LmPhysicalState
        {
            public bool RegimeServiceExists { get; set; }
            public bool YeniseiServiceExists { get; set; }
            public bool UninstallRegistryExists { get; set; }
            public bool InstallFolderExists { get; set; }
            public string InstallLocation { get; set; }
            public string Version { get; set; }
            public bool IsInstalled { get; set; }
        }

        private class LmRegistryInfo
        {
            public string DisplayVersion { get; set; }
            public string InstallLocation { get; set; }
        }
    }
}
