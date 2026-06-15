using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using HonestFlow.Infrastructure.Services;

namespace HonestFlow.Infrastructure.Installers
{
    public class LmModuleInstaller
    {
        private const int MsiSuccess = 0;
        private const int MsiRestartRequired = 3010;
        private const int MsiAnotherInstallInProgress = 1618;
        private const int MsiProductNotInstalled = 1605;

        // Важно: НЕ считаем любой msiexec.exe блокером.
        // Windows Installer часто держит фоновый msiexec.exe даже когда реальной установки уже нет.
        // Если ждать все msiexec.exe, чистая установка может зависнуть на несколько минут или до таймаута.
        // Реальную занятость MSI ловим по ExitCode=1618 и делаем retry.
        private static readonly string[] MsiBlockingProcessNames =
        {
            "InstallAutoUpdateLM",
            "AutoUpdateLM"
        };

        private static readonly string[] LmRuntimeProcessNames =
        {
            "Regime",
            "UpdateLM",
            "AutoUpdateLM"
        };

        private readonly string _installerPath;

        public LmModuleInstaller(string installerPath)
        {
            _installerPath = installerPath;
        }

        public string GetInstalledGuid() => FindLmModuleGuid();

        public async Task CleanInstall()
        {
            using var operation = Logger.BeginOperation("Чистая установка ЛМ ЧЗ", nameof(LmModuleInstaller));

            string existingGuid = FindLmModuleGuid();
            if (!string.IsNullOrWhiteSpace(existingGuid))
            {
                Logger.Warning($"Запрошена чистая установка, но GUID уже найден: {existingGuid}. Переключаемся на переустановку.", nameof(LmModuleInstaller));
                await ReinstallExisting("ЛМ уже установлен перед чистой установкой");
                return;
            }

            Logger.Info("ЛМ ЧЗ не найден по GUID. Запуск чистой установки.", nameof(LmModuleInstaller));
            await Install();
            Logger.Success("Чистая установка ЛМ ЧЗ завершена успешно", nameof(LmModuleInstaller));
        }

        public async Task ReinstallExisting(string reason)
        {
            using var operation = Logger.BeginOperation($"Переустановка ЛМ ЧЗ: {reason}", nameof(LmModuleInstaller));

            string guid = FindLmModuleGuid();
            if (!string.IsNullOrWhiteSpace(guid))
            {
                Logger.Info($"Найден GUID ЛМ ЧЗ: {guid}. Причина переустановки: {reason}", nameof(LmModuleInstaller));
                await Uninstall(guid, reason);
            }
            else
            {
                Logger.Warning($"GUID ЛМ ЧЗ не найден перед переустановкой. Причина: {reason}. Переходим к чистой установке.", nameof(LmModuleInstaller));
                await WaitForMsiSystemIdleAsync("перед установкой, GUID уже отсутствует", 120);
            }

            await Install();
            Logger.Success($"Переустановка ЛМ ЧЗ завершена успешно. Причина: {reason}", nameof(LmModuleInstaller));
        }

        public async Task ReinstallBecauseInnMismatch(string currentInn, string expectedInn)
        {
            string reason = $"INN mismatch: current={MaskInn(currentInn)}, expected={MaskInn(expectedInn)}";
            using var operation = Logger.BeginOperation($"Forced reinstall ЛМ ЧЗ из-за INN mismatch", nameof(LmModuleInstaller));

            Logger.Warning($"Запрошена переустановка ЛМ ЧЗ из-за конфликта ИНН: {reason}", nameof(LmModuleInstaller));
            await ReinstallExisting(reason);
        }

        private static async Task Uninstall(string guid, string reason)
        {
            using var operation = Logger.BeginOperation("Удаление ЛМ ЧЗ", nameof(LmModuleInstaller));

            Logger.Info($"Удаление ЛМ ЧЗ: {guid}. Причина: {reason}", nameof(LmModuleInstaller));

            await WindowsServiceManager.StopService();
            await Task.Delay(1000);

            KillProcess("InstallAutoUpdateLM");
            KillProcess("AutoUpdateLM");
            KillProcess("UpdateLM");
            KillProcess("Regime");

            await WaitForMsiSystemIdleAsync("перед удалением ЛМ ЧЗ", 120);

            int exitCode = await RunMsiWithRetryAsync(
                arguments: $"/x {guid} /qn /norestart",
                actionName: "удаление ЛМ ЧЗ",
                acceptedExitCodes: new[] { MsiSuccess, MsiRestartRequired, MsiProductNotInstalled });

            Logger.Info($"msiexec uninstall exit code: {exitCode}", nameof(LmModuleInstaller));

            if (exitCode == MsiProductNotInstalled)
            {
                Logger.Warning("MSI сообщает, что продукт уже не установлен. Продолжаем.", nameof(LmModuleInstaller));
            }

            await WaitForFullUninstall(guid, 180);
            await WaitForMsiSystemIdleAsync("после удаления ЛМ ЧЗ", 180);
        }

        private async Task Install()
        {
            await InstallCore();
            await VerifyUpdateLmWithSingleReinstall();
        }

        private async Task InstallCore()
        {
            using var operation = Logger.BeginOperation("Установка ЛМ ЧЗ", nameof(LmModuleInstaller));

            if (string.IsNullOrWhiteSpace(_installerPath) || !File.Exists(_installerPath))
                throw new FileNotFoundException("Не найден MSI установщик ЛМ ЧЗ", _installerPath);

            Logger.Info($"Установка {Path.GetFileName(_installerPath)}...", nameof(LmModuleInstaller));

            await WaitForLmChildInstallersIdleAsync("перед установкой ЛМ ЧЗ", 60);

            int exitCode = await RunMsiWithRetryAsync(
                arguments: $"/i \"{_installerPath}\" /qn /norestart",
                actionName: "установка ЛМ ЧЗ",
                acceptedExitCodes: new[] { MsiSuccess, MsiRestartRequired });

            Logger.Info($"msiexec install exit code: {exitCode}", nameof(LmModuleInstaller));

            if (exitCode == MsiRestartRequired)
            {
                Logger.Warning("MSI вернул 3010: установка успешна, но требуется перезагрузка", nameof(LmModuleInstaller));
            }

            await WaitForLmChildInstallersIdleAsync("после установки ЛМ ЧЗ / дочернего MSI", 120);
            await WaitForInstalledState(180);
        }

        private async Task VerifyUpdateLmWithSingleReinstall()
        {
            bool lmInstalled = IsLmInstalled();
            bool updateLmInstalled = await WaitForUpdateLmInstalled(30);

            LogInstallationState(lmInstalled, updateLmInstalled, false);

            if (!lmInstalled || updateLmInstalled)
                return;

            Logger.Warning(
                "ЛМ ЧЗ установлен, но служба UpdateLM отсутствует. Выполняется одна попытка переустановки ЛМ.",
                nameof(LmModuleInstaller));
            LogInstallationState(lmInstalled, updateLmInstalled, true);

            string guid = FindLmModuleGuid();
            await Uninstall(guid, "служба UpdateLM отсутствует после установки ЛМ");
            await InstallCore();

            lmInstalled = IsLmInstalled();
            updateLmInstalled = await WaitForUpdateLmInstalled(30);
            LogInstallationState(lmInstalled, updateLmInstalled, true);

            if (lmInstalled && !updateLmInstalled)
            {
                const string warning =
                    "ЛМ ЧЗ установлен и может продолжить работу, но служба автообновления UpdateLM не установлена. " +
                    "Автоматическое обновление ЛМ может быть недоступно.";

                Logger.Warning(warning, nameof(LmModuleInstaller));
                MessageBox.Show(
                    warning,
                    "Предупреждение UpdateLM",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private static async Task<int> RunMsiWithRetryAsync(string arguments, string actionName, int[] acceptedExitCodes)
        {
            const int maxAttempts = 3;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (attempt > 1)
                {
                    Logger.Warning($"Повтор MSI: {actionName}, попытка {attempt}/{maxAttempts}", nameof(LmModuleInstaller));
                }

                await WaitForMsiSystemIdleAsync($"перед MSI: {actionName}", 120);

                int exitCode = await RunMsiOnceAsync(arguments, actionName, attempt);

                if (acceptedExitCodes.Contains(exitCode))
                    return exitCode;

                if (exitCode == MsiAnotherInstallInProgress)
                {
                    Logger.Warning("MSI вернул 1618: уже выполняется другая установка. Ждём освобождения Windows Installer.", nameof(LmModuleInstaller));
                    await WaitForMsiSystemIdleAsync("после 1618", 180);
                    await Task.Delay(5000);
                    continue;
                }

                throw new Exception($"Ошибка MSI: {actionName}, код: {exitCode}");
            }

            throw new Exception($"Ошибка MSI: {actionName}, код 1618 не исчез после {maxAttempts} попыток");
        }

        private static async Task<int> RunMsiOnceAsync(string arguments, string actionName, int attempt)
        {
            Logger.Info($"Запуск msiexec: {actionName}, попытка {attempt}. Аргументы: {arguments}", nameof(LmModuleInstaller));

            var psi = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                throw new Exception($"Не удалось запустить msiexec: {actionName}");

            await Task.Run(() => process.WaitForExit());

            Logger.Info($"msiexec завершён: {actionName}, ExitCode={process.ExitCode}", nameof(LmModuleInstaller));
            return process.ExitCode;
        }

        private static async Task WaitForFullUninstall(string guid, int timeoutSeconds = 180)
        {
            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            int attempt = 0;

            Logger.Info($"Ожидание полного удаления ЛМ ЧЗ, таймаут {timeoutSeconds} сек", nameof(LmModuleInstaller));

            while (DateTime.Now - startTime < timeout)
            {
                attempt++;

                bool guidExists = IsGuidInstalled(guid);
                string regimeServiceStatus = await WindowsServiceManager.GetServiceStatus();
                bool updateServiceExists = IsUpdateLmServiceInstalled();
                bool msiBusy = IsAnyProcessRunning(MsiBlockingProcessNames);
                bool runtimeRunning = IsAnyProcessRunning(LmRuntimeProcessNames);

                bool regimeServiceAbsent = IsServiceAbsent(regimeServiceStatus);

                if (!guidExists && regimeServiceAbsent && !updateServiceExists && !msiBusy && !runtimeRunning)
                {
                    Logger.Success($"Полное удаление ЛМ ЧЗ подтверждено за {(DateTime.Now - startTime).TotalSeconds:F1} сек: GUID отсутствует, Regime отсутствует, UpdateLM отсутствует, дочерние установщики завершены", nameof(LmModuleInstaller));
                    return;
                }

                if (attempt == 1 || attempt % 5 == 0)
                {
                    Logger.DebugLog($"ЛМ ещё удаляется: guidExists={guidExists}, regimeService={regimeServiceStatus}, updateServiceExists={updateServiceExists}, msiBusy={msiBusy}, runtimeRunning={runtimeRunning}", nameof(LmModuleInstaller));
                }

                await Task.Delay(2000);
            }

            Logger.Warning($"Таймаут ожидания полного удаления ЛМ ЧЗ ({timeoutSeconds} сек). Продолжаем осторожно.", nameof(LmModuleInstaller));
        }

        private static async Task WaitForInstalledState(int timeoutSeconds = 180)
        {
            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            int attempt = 0;

            Logger.Info($"Ожидание установленного состояния ЛМ ЧЗ, таймаут {timeoutSeconds} сек", nameof(LmModuleInstaller));

            while (DateTime.Now - startTime < timeout)
            {
                attempt++;

                string guid = FindLmModuleGuid();
                bool regimeServiceInstalled = IsRegimeServiceInstalled();
                bool childInstallerRunning = IsAnyProcessRunning(MsiBlockingProcessNames);

                if (!string.IsNullOrWhiteSpace(guid) && regimeServiceInstalled)
                {
                    Logger.Success(
                        $"Установка ЛМ ЧЗ подтверждена за {(DateTime.Now - startTime).TotalSeconds:F1} сек: guid={guid}, Regime Installed=true",
                        nameof(LmModuleInstaller));
                    return;
                }

                if (attempt == 1 || attempt % 5 == 0)
                {
                    Logger.DebugLog(
                        $"Ожидание установленного состояния: guid={(string.IsNullOrWhiteSpace(guid) ? "нет" : guid)}, Regime Installed={regimeServiceInstalled}, childInstallerRunning={childInstallerRunning}",
                        nameof(LmModuleInstaller));
                }

                await Task.Delay(2000);
            }

            throw new TimeoutException($"ЛМ ЧЗ не перешёл в установленное состояние за {timeoutSeconds} сек");
        }

        private static async Task<bool> WaitForUpdateLmInstalled(int timeoutSeconds)
        {
            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);

            while (DateTime.Now - startTime < timeout)
            {
                if (IsUpdateLmServiceInstalled())
                    return true;

                await Task.Delay(2000);
            }

            return IsUpdateLmServiceInstalled();
        }

        private static bool IsLmInstalled()
        {
            return !string.IsNullOrWhiteSpace(FindLmModuleGuid()) &&
                   IsRegimeServiceInstalled();
        }

        private static bool IsRegimeServiceInstalled()
        {
            return RegistryKeyExists(@"SYSTEM\CurrentControlSet\Services\Regime");
        }

        private static void LogInstallationState(
            bool lmInstalled,
            bool updateLmInstalled,
            bool reinstallAttempt)
        {
            Logger.Info(
                $"LM Installed = {lmInstalled.ToString().ToLowerInvariant()}; " +
                $"UpdateLM Installed = {updateLmInstalled.ToString().ToLowerInvariant()}; " +
                $"Reinstall Attempt = {reinstallAttempt.ToString().ToLowerInvariant()}",
                nameof(LmModuleInstaller));
        }

        private static bool IsServiceAbsent(string serviceStatus)
        {
            if (string.IsNullOrWhiteSpace(serviceStatus))
                return true;

            return serviceStatus.Equals("notfound", StringComparison.OrdinalIgnoreCase) ||
                   serviceStatus.Equals("not found", StringComparison.OrdinalIgnoreCase) ||
                   serviceStatus.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
                   serviceStatus.Equals("absent", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsServicePresent(string serviceStatus) => !IsServiceAbsent(serviceStatus);

        private static async Task WaitForMsiSystemIdleAsync(string reason, int timeoutSeconds)
        {
            // Оставляем метод для мест, где логически ждём MSI, но не блокируемся на фоновый msiexec.exe.
            // Реальное состояние Windows Installer достовернее проверяется через код 1618 после запуска msiexec.
            await WaitForLmChildInstallersIdleAsync(reason, timeoutSeconds);
        }

        private static async Task WaitForLmChildInstallersIdleAsync(string reason, int timeoutSeconds)
        {
            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            var quietStart = (DateTime?)null;
            int attempt = 0;

            while (DateTime.Now - startTime < timeout)
            {
                attempt++;
                var running = GetRunningProcesses(MsiBlockingProcessNames);

                if (running.Length == 0)
                {
                    quietStart ??= DateTime.Now;

                    if ((DateTime.Now - quietStart.Value).TotalSeconds >= 3)
                    {
                        if (attempt > 1)
                            Logger.Success($"Дочерние установщики ЛМ завершены: {reason}", nameof(LmModuleInstaller));
                        return;
                    }
                }
                else
                {
                    quietStart = null;

                    if (attempt == 1 || attempt % 5 == 0)
                    {
                        Logger.Info($"Ожидание дочерних установщиков ЛМ: {reason}. Активны: {string.Join(", ", running)}", nameof(LmModuleInstaller));
                    }
                }

                await Task.Delay(1000);
            }

            Logger.Warning($"Таймаут ожидания дочерних установщиков ЛМ: {reason}. Продолжаем: состояние ЛМ будет проверено по GUID/службам/API.", nameof(LmModuleInstaller));
        }

        private static string MaskInn(string inn)
        {
            if (string.IsNullOrWhiteSpace(inn) || inn.Length < 6)
                return inn ?? string.Empty;

            return inn.Substring(0, 4) + new string('*', Math.Max(0, inn.Length - 6)) + inn.Substring(inn.Length - 2);
        }

        private static bool RegistryKeyExists(string regPath)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(regPath);
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsGuidInstalled(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
                return false;

            string[] registryPaths =
            {
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{guid}",
                $@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{guid}"
            };

            return registryPaths.Any(RegistryKeyExists);
        }

        private static bool IsAnyProcessRunning(string[] processNames) => GetRunningProcesses(processNames).Length > 0;

        private static string[] GetRunningProcesses(string[] processNames)
        {
            return processNames
                .SelectMany(name =>
                {
                    try
                    {
                        return Process.GetProcessesByName(name)
                            .Select(p => $"{name}.exe:{p.Id}")
                            .ToArray();
                    }
                    catch
                    {
                        return Array.Empty<string>();
                    }
                })
                .ToArray();
        }

        private static void KillProcess(string processName)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                foreach (var p in processes)
                {
                    Logger.Warning($"Завершаем {processName}.exe (PID: {p.Id})", nameof(LmModuleInstaller));
                    p.Kill();
                    p.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Ошибка завершения процесса {processName}: {ex.Message}", nameof(LmModuleInstaller));
            }
        }

        private static bool IsUpdateLmServiceInstalled()
        {
            try
            {
                using var servicesRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
                if (servicesRoot == null)
                    return false;

                foreach (string serviceName in servicesRoot.GetSubKeyNames())
                {
                    if (serviceName.Contains("UpdateLM", StringComparison.OrdinalIgnoreCase) ||
                        serviceName.Contains("AutoUpdateLM", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    using var serviceKey = servicesRoot.OpenSubKey(serviceName);
                    string imagePath = serviceKey?.GetValue("ImagePath")?.ToString() ?? string.Empty;
                    string displayName = serviceKey?.GetValue("DisplayName")?.ToString() ?? string.Empty;

                    if (imagePath.Contains("UpdateLM", StringComparison.OrdinalIgnoreCase) ||
                        imagePath.Contains("AutoUpdateLM", StringComparison.OrdinalIgnoreCase) ||
                        displayName.Contains("UpdateLM", StringComparison.OrdinalIgnoreCase) ||
                        displayName.Contains("AutoUpdateLM", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.DebugLog($"Не удалось проверить службу UpdateLM: {ex.Message}", nameof(LmModuleInstaller));
            }

            return false;
        }

        private static string FindLmModuleGuid()
        {
            try
            {
                string guid = FindLmModuleGuidInRegistry(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (!string.IsNullOrEmpty(guid))
                    return guid;

                return FindLmModuleGuidInRegistry(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
            }
            catch (Exception ex)
            {
                Logger.Warning($"Ошибка поиска GUID ЛМ ЧЗ: {ex.Message}", nameof(LmModuleInstaller));
                return null;
            }
        }

        private static string FindLmModuleGuidInRegistry(string uninstallRoot)
        {
            using var key = Registry.LocalMachine.OpenSubKey(uninstallRoot);
            if (key == null)
                return null;

            foreach (string subKeyName in key.GetSubKeyNames())
            {
                using var subKey = key.OpenSubKey(subKeyName);
                string displayName = subKey?.GetValue("DisplayName")?.ToString() ?? string.Empty;

                if (displayName.Contains("Локальный модуль", StringComparison.OrdinalIgnoreCase) ||
                    displayName.Contains("Regime", StringComparison.OrdinalIgnoreCase))
                {
                    return subKeyName;
                }
            }

            return null;
        }
    }
}
