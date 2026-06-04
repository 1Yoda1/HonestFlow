using ESM_Installer_SPI.Classes;
using ESM_Installer_SPI.Models;
using HonestFlow.Helpers;
using HonestFlow.Infrastructure;
using HonestFlow.Services.Core;
using HonestFlow.Services.Lm;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HonestFlow.Services.Installation
{
    /// <summary>
    /// Реализация сервиса установки
    /// </summary>
    public class InstallationService : IInstallationService
    {
        private readonly ILogService _log;
        private readonly IProgressService _progress;
        private readonly LmValidationService _lmValidator;
        private readonly VersionCheckService _versionChecker;

        public InstallationService(ILogService logService, IProgressService progressService)
        {
            _log = logService;
            _progress = progressService;
            _lmValidator = new LmValidationService(_log);
            _versionChecker = new VersionCheckService(_log);
        }

        public async Task<bool> CheckLmAndInstall(IPData selectedIP)
        {
            _progress.SetProgress(5, "Проверка локального модуля...");

            try
            {
                var versions = ConfigManager.LoadVersions();
                string expectedVersion = versions?.lm_module ?? "2.5.1-2";
                _log.LogDebug($"Ожидаемая версия ЛМ ЧЗ: {expectedVersion}");

                var status = await _lmValidator.GetLmStatus(expectedVersion);

                if (status == null)
                {
                    _log.LogUser("ЛМ ЧЗ: не установлен", true);
                    _log.LogDebug("ЛМ ЧЗ не установлен или не отвечает");
                    await PerformInstallation(selectedIP);
                    return true;
                }

                _log.LogUser($"ЛМ ЧЗ: версия {status.version}, статус: {status.status}");
                _log.LogDebug($"ЛМ активен: версия={status.version}, статус={status.status}, inn={status.inn ?? "не задан"}");

                // Проверка ИНН
                if (!string.IsNullOrEmpty(selectedIP.Inn) && !string.IsNullOrEmpty(status.inn) && status.inn != selectedIP.Inn)
                {
                    var result = await HandleInnMismatch(selectedIP, status.inn);
                    if (!result) return false;
                }

                await PerformInstallation(selectedIP);
                return true;
            }
            catch (Exception ex)
            {
                _log.LogUser($"Ошибка: {ex.Message}", true);
                _log.LogDebug($"Ошибка при проверке ЛМ: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private async Task<bool> HandleInnMismatch(IPData selectedIP, string currentInn)
        {
            _log.LogUser($"ИНН не совпадает! (ЛМ: {currentInn}, нужно: {selectedIP.Inn})", true);
            _log.LogDebug("КРИТИЧЕСКАЯ ОШИБКА: ИНН не совпадает!");

            var result = MessageBox.Show(
                $"ЛМ ЧЗ инициализирован на ИНН: {currentInn}\n\nОжидался ИНН: {selectedIP.Inn}\n\n" +
                $"Установка НЕ МОЖЕТ быть продолжена!\n\nНажмите 'OK' чтобы открыть окно удаления программ.\n" +
                $"Найдите в списке 'Локальный модуль ЧЗ' и удалите его.\n\nПосле удаления нажмите 'OK' для повторной проверки.",
                "Конфликт ИНН", MessageBoxButtons.OKCancel, MessageBoxIcon.Error);

            if (result == DialogResult.Cancel)
            {
                _log.LogUser("Операция отменена пользователем");
                return false;
            }

            ProcessRunner.OpenUninstallPrograms();
            _log.LogUser("Ожидание удаления ЛМ ЧЗ...");

            // Активное ожидание удаления
            var maxWaitSeconds = 120;
            var checkIntervalSeconds = 5;
            var elapsedSeconds = 0;
            bool wasDeleted = false;

            // Получаем ожидаемую версию (можно захардкодить или взять из ConfigManager)
            var versions = ConfigManager.LoadVersions();
            string expectedVersion = versions?.lm_module ?? "2.5.1-2";

            while (elapsedSeconds < maxWaitSeconds)
            {
                await Task.Delay(checkIntervalSeconds * 1000);
                elapsedSeconds += checkIntervalSeconds;

                _log.LogDebug($"Проверка наличия ЛМ ЧЗ... (прошло {elapsedSeconds} сек)");

                var checkAgain = await _lmValidator.GetLmStatus(expectedVersion);
                if (checkAgain == null)
                {
                    _log.LogUser($"✅ ЛМ ЧЗ удалён (через {elapsedSeconds} сек)");
                    wasDeleted = true;
                    break;
                }
            }

            if (!wasDeleted)
            {
                var retry = MessageBox.Show(
                    $"ЛМ ЧЗ всё ещё обнаружен (ожидание {maxWaitSeconds} сек).\n\n" +
                    "Удалите его вручную через 'Установку и удаление программ'.\n\n" +
                    "Нажмите 'OK' для повторной проверки или 'Отмена' для выхода.",
                    "Повторить?", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

                if (retry == DialogResult.OK)
                    return await HandleInnMismatch(selectedIP, currentInn);

                return false;
            }

            _log.LogUser("ЛМ ЧЗ удалён");
            return true;
        }

        private async Task PerformInstallation(IPData selectedIP)
        {
            _progress.SetProgress(0, "Начало проверки...");

            string installersFolder = ConfigManager.GetInstallersFolder();
            _log.LogDebug($"Папка с установщиками: {installersFolder}");

            var versions = ConfigManager.LoadVersions();

            string FindFile(string mask) => Directory.GetFiles(installersFolder, mask).FirstOrDefault();

            string lmPath = FindFile("regime-*.msi");
            string atolPath = FileHelper.GetAtolInstallerByArchitecture(installersFolder, selectedIP.Architecture, _log);
            string esmPath = FindFile("esm_*.exe");
            string controllerPath = FindFile("esm-lm-controller_*.exe");

            _log.LogDebug($"Разрядность ИП: {selectedIP.Architecture}");
            _log.LogDebug($"Найдены: ЛМ={Path.GetFileName(lmPath) ?? "нет"}, АТОЛ={Path.GetFileName(atolPath) ?? "нет"}, " +
                    $"ЕСМ={Path.GetFileName(esmPath) ?? "нет"}, Контроллер={Path.GetFileName(controllerPath) ?? "нет"}");

            _progress.SetProgress(20, "Проверка версий...");

            var (NeedInstall, DisplayStatus) = await _lmValidator.GetLmStatusInfo(versions?.lm_module);
            bool needLmInstall = NeedInstall;
            bool needAtolInstall = _versionChecker.NeedAtolInstall(selectedIP, versions?.atol_driver);
            bool needEsmInstall = _versionChecker.NeedEsmInstall(versions?.esm);
            bool needControllerInstall = _versionChecker.NeedControllerInstall(versions?.controller);

            _log.LogUser("");
            _log.LogUser("=== РЕЗУЛЬТАТ ===");
            _log.LogUser($"ЛМ ЧЗ: {DisplayStatus}");
            _log.LogUser($"Драйвер АТОЛ: {(needAtolInstall ? "❌ требуется установка" : "✅ OK")}");
            _log.LogUser($"ЕСМ: {(needEsmInstall ? "❌ требуется установка" : "✅ OK")}");
            _log.LogUser($"Контроллер: {(needControllerInstall ? "❌ требуется установка" : "✅ OK")}");
            _log.LogUser("=================");

            if (!needLmInstall && !needAtolInstall && !needEsmInstall && !needControllerInstall)
            {
                _progress.SetProgress(100, "Готово");
                _log.LogUser("✅ Все компоненты уже установлены!");
                MessageBox.Show("Все компоненты уже установлены и соответствуют требованиям!", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int totalSteps = (needLmInstall ? 1 : 0) + (needAtolInstall ? 1 : 0) + (needEsmInstall ? 1 : 0) + (needControllerInstall ? 1 : 0);
            int currentStep = 0;
            int stepPercent = 70 / totalSteps;

            _log.LogUser("=== НАЧАЛО УСТАНОВКИ ===");

            if (needLmInstall)
            {
                currentStep++;
                _progress.SetProgress(25 + (currentStep * stepPercent), "Установка ЛМ ЧЗ...");
                if (lmPath == null) throw new Exception("Не найден установщик ЛМ ЧЗ");
                _log.LogUser("Установка ЛМ ЧЗ...");
                _log.LogDebug($"Запуск: {lmPath}");
                var lm = new LmModule(lmPath, versions?.lm_module ?? "2.5.1-2");
                await lm.EnsureInstalledAndInitialized(selectedIP.Token, selectedIP.Inn);
                _log.LogUser("✅ ЛМ ЧЗ установлен");
            }

            if (needAtolInstall)
            {
                currentStep++;
                _progress.SetProgress(25 + (currentStep * stepPercent), "Установка драйвера АТОЛ...");
                if (atolPath != null)
                {
                    _log.LogUser("Установка драйвера АТОЛ...");
                    _log.LogDebug($"Запуск: {atolPath}");
                    await new AtolInstaller(atolPath).Install();
                    _log.LogUser("✅ Драйвер АТОЛ установлен");
                }
                else
                {
                    _log.LogUser("⚠️ Драйвер АТОЛ не найден", true);
                }
            }

            if (needEsmInstall || needControllerInstall)
            {
                currentStep++;
                _progress.SetProgress(25 + (currentStep * stepPercent), "Установка ЕСМ и Контроллера...");
                if (esmPath != null && controllerPath != null)
                {
                    var esm = new EsmInstaller(esmPath, controllerPath);
                    if (needEsmInstall)
                    {
                        _log.LogUser("Установка ЕСМ...");
                        _log.LogDebug($"Запуск: {esmPath}");
                        await esm.InstallEsm();
                        _log.LogUser("✅ ЕСМ установлен");
                    }
                    if (needControllerInstall)
                    {
                        _log.LogUser("Установка Контроллера...");
                        _log.LogDebug($"Запуск: {controllerPath}");
                        await esm.InstallController();
                        _log.LogUser("✅ Контроллер установлен");
                    }
                }
                else
                {
                    _log.LogUser("⚠️ ЕСМ или Контроллер не найдены", true);
                }
            }

            _progress.SetProgress(100, "Установка завершена!");
            _log.LogUser("✅ Установка завершена!");
            MessageBox.Show("Установка завершена!", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}