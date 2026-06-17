using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HonestFlow.Helpers;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Infrastructure.Installers;
using HonestFlow.Models;
using HonestFlow.Services.Core;
using HonestFlow.Services.Installation.Planning;
using HonestFlow.Services.Lm;

namespace HonestFlow.Services.Installation
{
    /// <summary>
    /// Главный сценарий установки.
    /// Здесь только оркестрация: проверка, план, скачивание, запуск установщиков.
    /// Детали конфигов, GitHub, путей, ЛМ и версий вынесены в отдельные классы.
    /// </summary>
    public class InstallationService : IInstallationService
    {
        private readonly ILogService _log;
        private readonly IProgressService _progress;
        private readonly ILmValidationService _lmValidator;
        private readonly IVersionCheckService _versionChecker;
        private readonly IUserDialogService _dialogService;
        private readonly bool _useGitHubMode;

        public InstallationService(ILogService logService, IProgressService progressService, IUserDialogService dialogService, bool useGitHubMode = false)
        {
            _log = logService;
            _progress = progressService;
            _dialogService = dialogService ?? new WinFormsDialogService();
            _useGitHubMode = useGitHubMode;
            _lmValidator = new LmValidationService(_log);
            _versionChecker = new VersionCheckService(_log);
        }

        public async Task<bool> CheckLmAndInstall(IPData selectedIP)
        {
            _progress.SetProgress(5, "Проверка локального модуля...");

            try
            {
                _progress.SetProgress(6, "Загрузка конфигурации версий...");
                var versions = LoadVersions();
                string expectedLmVersion = EnsureLmVersionConfigured(versions);

                _log.LogDebug($"Ожидаемая версия ЛМ ЧЗ: {expectedLmVersion}");

                _progress.SetProgress(8, "Проверка статуса ЛМ ЧЗ...");
                var lmCheck = await _lmValidator.CheckLmStatus(expectedLmVersion);
                var status = lmCheck.ApiStatus;

                _log.LogUser($"ЛМ ЧЗ: {lmCheck.DisplayStatus}");
                _log.LogDebug(
                    $"ЛМ ЧЗ audit: installed={lmCheck.IsPhysicallyInstalled}, " +
                    $"physicalVersion={lmCheck.PhysicalVersion ?? "не определена"}, " +
                    $"runtime={lmCheck.RuntimeStatus}, diagnostics={lmCheck.DiagnosticStatus}, " +
                    $"needsInstall={lmCheck.NeedsInstall}, needsInitialize={lmCheck.NeedsInitialize}");

                bool forceLmInstall = false;
                string lmPlanReason = null;

                if (status != null &&
                    !string.IsNullOrEmpty(selectedIP.Inn) &&
                    !string.IsNullOrEmpty(status.Inn) &&
                    status.Inn != selectedIP.Inn)
                {
                    forceLmInstall = true;
                    lmPlanReason = $"INN mismatch: в ЛМ {MaskInnForLog(status.Inn)}, ожидается {MaskInnForLog(selectedIP.Inn)}";

                    _log.LogUser($"ИНН ЛМ ЧЗ не совпадает: в ЛМ {MaskInnForLog(status.Inn)}, нужно {MaskInnForLog(selectedIP.Inn)}", true);
                    _log.LogDebug($"ЛМ ЧЗ будет передан в ветку forced reinstall из-за INN mismatch. {lmPlanReason}");
                }

                return await PerformInstallation(selectedIP, versions, lmCheck, forceLmInstall, lmPlanReason);
            }
            catch (Exception ex)
            {
                _log.LogUser($"Ошибка: {ex.Message}", true);
                _log.LogDebug($"Ошибка при проверке ЛМ: {ex.Message}\n{ex.StackTrace}");
                _dialogService.ShowError($"Ошибка: {ex.Message}", "Ошибка");
                return false;
            }
        }

        private VersionsData LoadVersions()
        {
            return _useGitHubMode
                ? ConfigManager.LoadVersionsFromGitHub()
                : ConfigManager.LoadVersions();
        }

        /// <summary>
        /// Версия ЛМ ЧЗ больше не имеет fallback в коде.
        /// Если версия не задана в конфигурации, установка должна остановиться с понятной ошибкой.
        /// </summary>
        private string EnsureLmVersionConfigured(VersionsData versions)
        {
            string lmVersion = versions?.LmModule;

            if (string.IsNullOrWhiteSpace(lmVersion))
            {
                const string message = "Версия ЛМ ЧЗ не задана в конфигурации. Проверьте versions.json / GitHub versions.";
                _log.LogUser($"❌ {message}", true);
                _log.LogDebug($"Ошибка конфигурации: {message}");
                throw new InvalidOperationException(message);
            }

            return lmVersion;
        }

        private static string MaskInnForLog(string inn)
        {
            if (string.IsNullOrWhiteSpace(inn) || inn.Length < 6)
                return inn ?? string.Empty;

            return inn.Substring(0, 4) + new string('*', Math.Max(0, inn.Length - 6)) + inn.Substring(inn.Length - 2);
        }

        private async Task<bool> PerformInstallation(
            IPData selectedIP,
            VersionsData versions,
            LmValidationResult precheckedLm,
            bool forceLmInstall,
            string lmPlanReason)
        {
            var effectiveVersions = ApplyClientVersionOverrides(selectedIP, versions);
            _progress.SetProgress(10, "Формирование плана установки...");
            var plan = await BuildInstallationPlan(selectedIP, effectiveVersions, precheckedLm, forceLmInstall, lmPlanReason);
            _progress.SetProgress(12, "Проверка версий и компонентов...");

            LogPlan(plan);

            if (!plan.HasWork)
            {
                _progress.SetProgress(100, "Готово");
                _log.LogUser("✅ Все компоненты уже установлены!");
                _dialogService.ShowInformation("Все компоненты уже установлены и соответствуют требованиям!", "Готово");
                return true;
            }

            _progress.SetProgress(15, "Подготовка установщиков...");
            if (!await ResolveInstallerPaths(plan, selectedIP, effectiveVersions)) return false;

            _progress.SetProgress(70, "Запуск установки компонентов...");
            bool success = await ExecuteInstallationPlan(plan, selectedIP, effectiveVersions);

            _progress.SetProgress(100, success ? "Установка завершена!" : "Установка завершена с ошибками");

            if (success)
            {
                _log.LogUser("✅ Установка завершена!");
                _dialogService.ShowInformation("Установка завершена!", "Готово");
            }
            else
            {
                _log.LogUser("❌ Установка завершена с ошибками", true);
            }

            return success;
        }

        private async Task<InstallationPlan> BuildInstallationPlan(

            IPData selectedIP,
            VersionsData versions,
            LmValidationResult precheckedLm,
            bool forceLmInstall,
            string lmPlanReason)
        {
            var plan = new InstallationPlan();
            var effectiveVersions = ApplyClientVersionOverrides(selectedIP, versions);
            string expectedLmVersion = EnsureLmVersionConfigured(effectiveVersions);

            bool needLmInstall;
            bool needLmInitialize = false;
            string lmStatusText;

            if (forceLmInstall)
            {
                needLmInstall = true;
                lmStatusText = lmPlanReason ?? "требуется переустановка ЛМ ЧЗ";
                _log.LogDebug($"План ЛМ ЧЗ: принудительная установка. Причина: {lmStatusText}");
            }
            else if (precheckedLm != null)
            {
                needLmInstall = precheckedLm.NeedsInstall;
                needLmInitialize = precheckedLm.NeedsInitialize;
                lmStatusText = precheckedLm.DisplayStatus;

                _log.LogDebug(
                    "План ЛМ ЧЗ построен по предварительному audit без повторного HTTP-запроса: " +
                    $"Installed={precheckedLm.IsPhysicallyInstalled}; " +
                    $"RuntimeStatus={precheckedLm.RuntimeStatus}; " +
                    $"DiagnosticStatus={precheckedLm.DiagnosticStatus}; " +
                    $"NeedsInstall={needLmInstall}; NeedsInitialize={needLmInitialize}; " +
                    $"Reason={precheckedLm.DecisionReason}");
            }
            else
            {
                var lmCheck = await _lmValidator.CheckLmStatus(expectedLmVersion);
                needLmInstall = lmCheck.NeedsInstall;
                needLmInitialize = lmCheck.NeedsInitialize;
                lmStatusText = lmCheck.DisplayStatus;

                _log.LogDebug(
                    "План ЛМ ЧЗ построен по audit: " +
                    $"Installed={lmCheck.IsPhysicallyInstalled}; " +
                    $"RuntimeStatus={lmCheck.RuntimeStatus}; " +
                    $"DiagnosticStatus={lmCheck.DiagnosticStatus}; " +
                    $"NeedsInstall={needLmInstall}; NeedsInitialize={needLmInitialize}; " +
                    $"Reason={lmCheck.DecisionReason}");
            }

            plan.Items.Add(new ComponentPlanItem
            {
                Component = InstallationComponent.LmModule,
                DisplayName = "ЛМ ЧЗ",
                NeedInstall = needLmInstall,
                NeedInitialize = needLmInitialize,
                StatusText = lmStatusText,
                ExpectedVersion = expectedLmVersion
            });

            bool needAtolInstall = _versionChecker.NeedAtolInstall(selectedIP, effectiveVersions?.AtolDriver);
            plan.Items.Add(new ComponentPlanItem
            {
                Component = InstallationComponent.AtolDriver,
                DisplayName = "Драйвер АТОЛ",
                NeedInstall = needAtolInstall,
                StatusText = needAtolInstall ? "требуется установка" : "OK",
                ExpectedVersion = effectiveVersions?.AtolDriver
            });

            bool needEsmInstall = _versionChecker.NeedEsmInstall(effectiveVersions?.ESM);
            plan.Items.Add(new ComponentPlanItem
            {
                Component = InstallationComponent.Esm,
                DisplayName = "ЕСМ",
                NeedInstall = needEsmInstall,
                StatusText = needEsmInstall ? "требуется установка" : "OK",
                ExpectedVersion = effectiveVersions?.ESM
            });

            bool needControllerInstall = _versionChecker.NeedControllerInstall(effectiveVersions?.Controller);
            plan.Items.Add(new ComponentPlanItem
            {
                Component = InstallationComponent.Controller,
                DisplayName = "Контроллер",
                NeedInstall = needControllerInstall,
                StatusText = needControllerInstall ? "требуется установка" : "OK",
                ExpectedVersion = effectiveVersions?.Controller
            });

            InstallerFileNameBuilder.FillFileNames(plan, selectedIP, effectiveVersions);
            return plan;
        }
        private VersionsData ApplyClientVersionOverrides(IPData selectedIP, VersionsData globalVersions)
        {
            var result = new VersionsData
            {
                LmModule = globalVersions?.LmModule,
                AtolDriver = globalVersions?.AtolDriver,
                ESM = globalVersions?.ESM,
                Controller = globalVersions?.Controller
            };

            if (selectedIP?.Versions == null)
                return result;

            if (!string.IsNullOrWhiteSpace(selectedIP.Versions.LmModule))
                result.LmModule = selectedIP.Versions.LmModule;

            if (!string.IsNullOrWhiteSpace(selectedIP.Versions.AtolDriver))
                result.AtolDriver = selectedIP.Versions.AtolDriver;

            if (!string.IsNullOrWhiteSpace(selectedIP.Versions.ESM))
                result.ESM = selectedIP.Versions.ESM;

            if (!string.IsNullOrWhiteSpace(selectedIP.Versions.Controller))
                result.Controller = selectedIP.Versions.Controller;

            return result;
        }

        private void LogPlan(InstallationPlan plan)
        {
            _log.LogUser("");
            _log.LogUser("=== ПЛАН УСТАНОВКИ ===");

            foreach (var item in plan.Items)
            {
                string marker = item.HasWork ? "❌" : "✅";
                _log.LogUser($"{item.DisplayName}: {marker} {item.StatusText}");
            }

            _log.LogUser("======================");
        }

        private async Task<bool> ResolveInstallerPaths(InstallationPlan plan, IPData selectedIP, VersionsData versions)
        {
            if (_useGitHubMode)
                return await DownloadAndResolveGitHubInstallers(plan);

            ResolveLocalInstallerPaths(plan, selectedIP);
            return ValidateRequiredInstallerPaths(plan);
        }

        private async Task<bool> DownloadAndResolveGitHubInstallers(InstallationPlan plan)
        {
            int total = plan.RequiredItems.Count(x => x.NeedInstall);
            if (total == 0)
                return ValidateRequiredInstallerPaths(plan);

            int completed = 0;

            foreach (var item in plan.RequiredItems)
            {
                if (!item.NeedInstall)
                    continue;

                completed++;
                int startPercent = 15 + (completed - 1) * 50 / total;
                int endPercent = 15 + completed * 50 / total;
                _progress.SetProgress(startPercent, $"Подготовка скачивания: {item.DisplayName}...");

                var downloadProgress = new Progress<int>(percent =>
                {
                    int currentPercent = startPercent + (endPercent - startPercent) * percent / 100;
                    _progress.SetProgress(currentPercent, $"Скачивание {item.DisplayName}: {percent}%");
                });

                bool downloaded = await ConfigManager.DownloadInstallerIfNeeded(item.FileName, downloadProgress);
                if (!downloaded)
                {
                    _log.LogUser($"❌ Не удалось скачать {item.DisplayName}: {item.FileName}", true);
                    return false;
                }

                item.InstallerPath = Path.Combine(AppPaths.GitHubCacheFolder, item.FileName);
                _progress.SetProgress(endPercent, $"Скачано: {item.DisplayName}");
            }

            return ValidateRequiredInstallerPaths(plan);
        }

        private void ResolveLocalInstallerPaths(InstallationPlan plan, IPData selectedIP)
        {
            string installersFolder = ConfigManager.GetInstallersFolder();
            _progress.SetProgress(15, $"Поиск установщиков: {installersFolder}");

            _log.LogDebug($"Разрядность ИП: {selectedIP.Architecture}");
            _log.LogDebug($"Папка установщиков: {installersFolder}");

            foreach (var item in plan.RequiredItems)
            {
                if (!item.NeedInstall)
                    continue;

                item.InstallerPath = item.Component switch
                {
                    InstallationComponent.LmModule => Directory.GetFiles(installersFolder, "regime-*.msi").FirstOrDefault(),
                    InstallationComponent.AtolDriver => FileHelper.GetAtolInstallerByArchitecture(installersFolder, selectedIP.Architecture, _log),
                    InstallationComponent.Esm => Directory.GetFiles(installersFolder, "esm_*.exe").FirstOrDefault(),
                    InstallationComponent.Controller => Directory.GetFiles(installersFolder, "esm-lm-controller_*.exe").FirstOrDefault(),
                    _ => null
                };
            }
        }

        private bool ValidateRequiredInstallerPaths(InstallationPlan plan)
        {
            _progress.SetProgress(65, "Проверка найденных установщиков...");

            foreach (var item in plan.RequiredItems)
            {
                if (!item.NeedInstall)
                    continue;

                if (string.IsNullOrWhiteSpace(item.InstallerPath) || !File.Exists(item.InstallerPath))
                {
                    _log.LogUser($"❌ Не найден установщик: {item.DisplayName} ({item.FileName ?? "локальный поиск"})", true);
                    return false;
                }

                _log.LogDebug($"Установщик {item.DisplayName}: {item.InstallerPath}");
            }

            return true;
        }

        private async Task<bool> ExecuteInstallationPlan(InstallationPlan plan, IPData selectedIP, VersionsData versions)
        {
            int total = plan.RequiredCount;
            int completed = 0;
            bool allSuccess = true;

            _log.LogUser("=== НАЧАЛО УСТАНОВКИ ===");

            foreach (var item in plan.RequiredItems)
            {
                completed++;
                int startPercent = 70 + (completed - 1) * 25 / total;
                int endPercent = 70 + completed * 25 / total;
                _progress.SetProgress(startPercent, $"{item.DisplayName}: подготовка...");

                bool success = await InstallComponent(item, selectedIP, versions, startPercent, endPercent);
                allSuccess &= success;
            }

            return allSuccess;
        }

        private async Task<bool> InstallComponent(ComponentPlanItem item, IPData selectedIP, VersionsData versions, int progressStart, int progressEnd)
        {
            _log.LogUser(item.NeedInstall
                ? $"Установка: {item.DisplayName}..."
                : $"Инициализация: {item.DisplayName}...");

            if (item.NeedInstall)
                _log.LogDebug($"Запуск: {item.InstallerPath}");

            switch (item.Component)
            {
                case InstallationComponent.LmModule:
                    string lmVersion = EnsureLmVersionConfigured(versions);
                    SetComponentProgress(progressStart, progressEnd, 5, "ЛМ ЧЗ: подготовка");
                    var lm = new LmModuleService(item.InstallerPath, lmVersion, _log, _progress, _dialogService, progressStart, progressEnd);
                    bool lmSuccess = await lm.EnsureInstalledAndInitialized(selectedIP.Token, selectedIP.Inn);
                    SetComponentProgress(progressStart, progressEnd, 100, lmSuccess ? "ЛМ ЧЗ: готов" : "ЛМ ЧЗ: ошибка");
                    _log.LogUser(lmSuccess ? "✅ ЛМ ЧЗ установлен" : "❌ ЛМ ЧЗ не установлен", !lmSuccess);
                    return lmSuccess;

                case InstallationComponent.AtolDriver:
                    bool with1C = selectedIP.HasTag("With1C");

                    if (with1C)
                        _log.LogUser("Тег With1C найден: драйвер АТОЛ будет установлен с параметром /With1C");

                    SetComponentProgress(progressStart, progressEnd, 20, "Драйвер АТОЛ: проверка установщика");
                    SetComponentProgress(progressStart, progressEnd, 45, "Драйвер АТОЛ: запуск установки");
                    bool atolSuccess = await new AtolInstaller(item.InstallerPath, _log, with1C).Install();
                    SetComponentProgress(progressStart, progressEnd, 100, atolSuccess ? "Драйвер АТОЛ: готов" : "Драйвер АТОЛ: ошибка");

                    _log.LogUser(atolSuccess ? "✅ Драйвер АТОЛ установлен" : "❌ Драйвер АТОЛ не установлен", !atolSuccess);
                    return atolSuccess;

                case InstallationComponent.Esm:
                    SetComponentProgress(progressStart, progressEnd, 20, "ЕСМ: проверка установщика");
                    SetComponentProgress(progressStart, progressEnd, 45, "ЕСМ: запуск установки");
                    bool esmSuccess = await new EsmInstaller(item.InstallerPath, null, _log).InstallEsm();
                    SetComponentProgress(progressStart, progressEnd, 100, esmSuccess ? "ЕСМ: готов" : "ЕСМ: ошибка");
                    _log.LogUser(esmSuccess ? "✅ ЕСМ установлен" : "❌ ЕСМ не установлен", !esmSuccess);
                    return esmSuccess;

                case InstallationComponent.Controller:
                    SetComponentProgress(progressStart, progressEnd, 20, "Контроллер ЛМ: проверка установщика");
                    SetComponentProgress(progressStart, progressEnd, 45, "Контроллер ЛМ: запуск установки");
                    bool controllerSuccess = await new EsmInstaller(null, item.InstallerPath, _log).InstallController();
                    SetComponentProgress(progressStart, progressEnd, 100, controllerSuccess ? "Контроллер ЛМ: готов" : "Контроллер ЛМ: ошибка");
                    _log.LogUser(controllerSuccess ? "✅ Контроллер установлен" : "❌ Контроллер не установлен", !controllerSuccess);
                    return controllerSuccess;

                default:
                    _log.LogUser($"❌ Неизвестный компонент: {item.Component}", true);
                    return false;
            }
        }

        private void SetComponentProgress(int startPercent, int endPercent, int componentPercent, string message)
        {
            int percent = startPercent + (endPercent - startPercent) * componentPercent / 100;
            _progress.SetProgress(percent, message);
        }
    }
}
