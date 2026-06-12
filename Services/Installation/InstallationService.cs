using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using HonestFlow.Helpers;
using HonestFlow.Infrastructure;
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
        private readonly bool _useGitHubMode;

        public InstallationService(ILogService logService, IProgressService progressService, bool useGitHubMode = false)
        {
            _log = logService;
            _progress = progressService;
            _useGitHubMode = useGitHubMode;
            _lmValidator = new LmValidationService(_log);
            _versionChecker = new VersionCheckService(_log);
        }

        public async Task<bool> CheckLmAndInstall(IPData selectedIP)
        {
            _progress.SetProgress(5, "Проверка локального модуля...");

            try
            {
                var versions = LoadVersions();
                string expectedLmVersion = EnsureLmVersionConfigured(versions);

                _log.LogDebug($"Ожидаемая версия ЛМ ЧЗ: {expectedLmVersion}");

                var status = await _lmValidator.GetLmStatus(expectedLmVersion);

                if (status == null)
                {
                    _log.LogUser("ЛМ ЧЗ: не установлен", true);
                    _log.LogDebug("ЛМ ЧЗ не установлен или не отвечает");
                    return await PerformInstallation(selectedIP, versions, null, false, null);
                }

                _log.LogUser($"ЛМ ЧЗ: версия {status.Version}, статус: {status.Status}");
                _log.LogDebug($"ЛМ активен: версия={status.Version}, статус={status.Status}, inn={status.Inn ?? "не задан"}");

                bool forceLmInstall = false;
                string lmPlanReason = null;

                if (!string.IsNullOrEmpty(selectedIP.Inn) &&
                    !string.IsNullOrEmpty(status.Inn) &&
                    status.Inn != selectedIP.Inn)
                {
                    forceLmInstall = true;
                    lmPlanReason = $"INN mismatch: в ЛМ {MaskInnForLog(status.Inn)}, ожидается {MaskInnForLog(selectedIP.Inn)}";

                    _log.LogUser($"ИНН ЛМ ЧЗ не совпадает: в ЛМ {MaskInnForLog(status.Inn)}, нужно {MaskInnForLog(selectedIP.Inn)}", true);
                    _log.LogDebug($"ЛМ ЧЗ будет передан в ветку forced reinstall из-за INN mismatch. {lmPlanReason}");
                }

                return await PerformInstallation(selectedIP, versions, status, forceLmInstall, lmPlanReason);
            }
            catch (Exception ex)
            {
                _log.LogUser($"Ошибка: {ex.Message}", true);
                _log.LogDebug($"Ошибка при проверке ЛМ: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            LmStatus precheckedLmStatus,
            bool forceLmInstall,
            string lmPlanReason)
        {
            _progress.SetProgress(10, "Проверка версий...");

            var plan = await BuildInstallationPlan(selectedIP, versions, precheckedLmStatus, forceLmInstall, lmPlanReason);
            LogPlan(plan);

            if (!plan.HasWork)
            {
                _progress.SetProgress(100, "Готово");
                _log.LogUser("✅ Все компоненты уже установлены!");
                MessageBox.Show("Все компоненты уже установлены и соответствуют требованиям!", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }

            if (!await ResolveInstallerPaths(plan, selectedIP, versions))
                return false;

            bool success = await ExecuteInstallationPlan(plan, selectedIP, versions);

            _progress.SetProgress(100, success ? "Установка завершена!" : "Установка завершена с ошибками");

            if (success)
            {
                _log.LogUser("✅ Установка завершена!");
                MessageBox.Show("Установка завершена!", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            LmStatus precheckedLmStatus,
            bool forceLmInstall,
            string lmPlanReason)
        {
            var plan = new InstallationPlan();
            string expectedLmVersion = EnsureLmVersionConfigured(versions);

            bool needLmInstall;
            string lmStatusText;

            if (forceLmInstall)
            {
                needLmInstall = true;
                lmStatusText = lmPlanReason ?? "требуется переустановка ЛМ ЧЗ";
                _log.LogDebug($"План ЛМ ЧЗ: принудительная установка. Причина: {lmStatusText}");
            }
            else if (precheckedLmStatus != null)
            {
                needLmInstall = precheckedLmStatus.Version != expectedLmVersion;
                lmStatusText = needLmInstall
                    ? $"версия {precheckedLmStatus.Version}, требуется {expectedLmVersion}"
                    : $"OK, версия {precheckedLmStatus.Version}";

                _log.LogDebug("План ЛМ ЧЗ построен по предварительной проверке, без повторного HTTP-запроса");
            }
            else
            {
                var lmInfo = await _lmValidator.GetLmStatusInfo(expectedLmVersion);
                needLmInstall = lmInfo.Item1;
                lmStatusText = lmInfo.Item2;
            }

            plan.Items.Add(new ComponentPlanItem
            {
                Component = InstallationComponent.LmModule,
                DisplayName = "ЛМ ЧЗ",
                NeedInstall = needLmInstall,
                StatusText = lmStatusText,
                ExpectedVersion = expectedLmVersion
            });

            bool needAtolInstall = _versionChecker.NeedAtolInstall(selectedIP, versions?.AtolDriver);
            plan.Items.Add(new ComponentPlanItem
            {
                Component = InstallationComponent.AtolDriver,
                DisplayName = "Драйвер АТОЛ",
                NeedInstall = needAtolInstall,
                StatusText = needAtolInstall ? "требуется установка" : "OK",
                ExpectedVersion = versions?.AtolDriver
            });

            bool needEsmInstall = _versionChecker.NeedEsmInstall(versions?.ESM);
            plan.Items.Add(new ComponentPlanItem
            {
                Component = InstallationComponent.Esm,
                DisplayName = "ЕСМ",
                NeedInstall = needEsmInstall,
                StatusText = needEsmInstall ? "требуется установка" : "OK",
                ExpectedVersion = versions?.ESM
            });

            bool needControllerInstall = _versionChecker.NeedControllerInstall(versions?.Controller);
            plan.Items.Add(new ComponentPlanItem
            {
                Component = InstallationComponent.Controller,
                DisplayName = "Контроллер",
                NeedInstall = needControllerInstall,
                StatusText = needControllerInstall ? "требуется установка" : "OK",
                ExpectedVersion = versions?.Controller
            });

            InstallerFileNameBuilder.FillFileNames(plan, selectedIP, versions);
            return plan;
        }

        private void LogPlan(InstallationPlan plan)
        {
            _log.LogUser("");
            _log.LogUser("=== ПЛАН УСТАНОВКИ ===");

            foreach (var item in plan.Items)
            {
                string marker = item.NeedInstall ? "❌" : "✅";
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
            int total = plan.RequiredCount;
            int completed = 0;

            foreach (var item in plan.RequiredItems)
            {
                completed++;
                _progress.SetProgress(completed * 70 / total, $"Скачивание: {item.DisplayName}...");

                bool downloaded = await ConfigManager.DownloadInstallerIfNeeded(item.FileName, null);
                if (!downloaded)
                {
                    _log.LogUser($"❌ Не удалось скачать {item.DisplayName}: {item.FileName}", true);
                    return false;
                }

                item.InstallerPath = Path.Combine(AppPaths.GitHubCacheFolder, item.FileName);
            }

            return ValidateRequiredInstallerPaths(plan);
        }

        private void ResolveLocalInstallerPaths(InstallationPlan plan, IPData selectedIP)
        {
            string installersFolder = ConfigManager.GetInstallersFolder();

            _log.LogDebug($"Разрядность ИП: {selectedIP.Architecture}");
            _log.LogDebug($"Папка установщиков: {installersFolder}");

            foreach (var item in plan.RequiredItems)
            {
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
            foreach (var item in plan.RequiredItems)
            {
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
                _progress.SetProgress(70 + completed * 25 / total, $"Установка: {item.DisplayName}...");

                bool success = await InstallComponent(item, selectedIP, versions);
                allSuccess &= success;
            }

            return allSuccess;
        }

        private async Task<bool> InstallComponent(ComponentPlanItem item, IPData selectedIP, VersionsData versions)
        {
            _log.LogUser($"Установка: {item.DisplayName}...");
            _log.LogDebug($"Запуск: {item.InstallerPath}");

            switch (item.Component)
            {
                case InstallationComponent.LmModule:
                    string lmVersion = EnsureLmVersionConfigured(versions);
                    var lm = new LmModuleService(item.InstallerPath, lmVersion, _log);
                    bool lmSuccess = await lm.EnsureInstalledAndInitialized(selectedIP.Token, selectedIP.Inn);
                    _log.LogUser(lmSuccess ? "✅ ЛМ ЧЗ установлен" : "❌ ЛМ ЧЗ не установлен", !lmSuccess);
                    return lmSuccess;

                case InstallationComponent.AtolDriver:
                    bool atolSuccess = await new AtolInstaller(item.InstallerPath, _log).Install();
                    _log.LogUser(atolSuccess ? "✅ Драйвер АТОЛ установлен" : "❌ Драйвер АТОЛ не установлен", !atolSuccess);
                    return atolSuccess;

                case InstallationComponent.Esm:
                    bool esmSuccess = await new EsmInstaller(item.InstallerPath, null, _log).InstallEsm();
                    _log.LogUser(esmSuccess ? "✅ ЕСМ установлен" : "❌ ЕСМ не установлен", !esmSuccess);
                    return esmSuccess;

                case InstallationComponent.Controller:
                    bool controllerSuccess = await new EsmInstaller(null, item.InstallerPath, _log).InstallController();
                    _log.LogUser(controllerSuccess ? "✅ Контроллер установлен" : "❌ Контроллер не установлен", !controllerSuccess);
                    return controllerSuccess;

                default:
                    _log.LogUser($"❌ Неизвестный компонент: {item.Component}", true);
                    return false;
            }
        }
    }
}
