using System;
using System.Threading.Tasks;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Infrastructure.Installers;
using HonestFlow.Infrastructure.Services;
using HonestFlow.Models;
using HonestFlow.Services.Core;

namespace HonestFlow.Services.Lm
{
    /// <summary>
    /// Главный сценарий ЛМ ЧЗ: проверка установленного состояния, обновление, forced reinstall и инициализация.
    /// Инфраструктурные детали остаются в LmApiClient/LmModuleInstaller/WindowsServiceManager.
    /// </summary>
    public class LmModuleService
    {
        private readonly LmApiClient _apiClient;
        private readonly LmModuleInstaller _installer;
        private readonly string _expectedVersion;
        private string _apiVersion = "v2";
        private readonly ILogService _log;
        private readonly IProgressService _progress;
        private readonly IUserDialogService _dialogService;
        private readonly int _progressStart;
        private readonly int _progressEnd;

        public LmModuleService(string installerPath, string expectedVersion)
    : this(installerPath, expectedVersion, null)
        {
        }

        /*
        private void LogUser(string message, bool isError = false)
        {
            if (_log != null)
                _log.LogUser(message, isError);
            else
                Logger.LogToFile(message, isError);
        }

        private void LogDebug(string message)
        {
            if (_log != null)
                _log.LogDebug(message);
            else
                Logger.DebugLog(message, nameof(LmModuleService));
        }
        */

        public LmModuleService(string installerPath, string expectedVersion, ILogService log)
            : this(installerPath, expectedVersion, log, null, null, 70, 95)
        {
        }

        public LmModuleService(string installerPath, string expectedVersion, ILogService log, IProgressService progress, IUserDialogService dialogService, int progressStart, int progressEnd)
        {
            if (string.IsNullOrWhiteSpace(expectedVersion))
                throw new ArgumentException("Версия ЛМ ЧЗ не задана.", nameof(expectedVersion));

            _apiClient = new LmApiClient(true);
            _dialogService = dialogService ?? new WinFormsDialogService();
            _installer = new LmModuleInstaller(installerPath, _dialogService);
            _expectedVersion = expectedVersion;
            _log = log ?? new LogService();
            _progress = progress;
            _progressStart = progressStart;
            _progressEnd = progressEnd;
        }

        public async Task<bool> IsApiAvailable()
        {
            return await _apiClient.IsApiAvailable();
        }

        public async Task<LmStatus> GetStatus()
        {
            var response = await _apiClient.GetStatus();
            return response.IsSuccess ? response.Data : null;
        }

        public async Task<ApiResponse<LmStatus>> GetStatusFull()
        {
            return await _apiClient.GetStatus();
        }

        public async Task<ApiSimpleResponse> InitializeFull(string token)
        {
            SetProgress(80, $"ЛМ ЧЗ: инициализация через API {_apiVersion}");
            _log.LogUser($" Инициализация (API {_apiVersion})...");
            return await _apiClient.InitializeFull(token);
        }

        public async Task<bool> EnsureInstalledAndInitialized(string token, string expectedInn)
        {
            using var operation = Logger.BeginOperation("EnsureInstalledAndInitialized ЛМ ЧЗ", nameof(LmModuleService));
            SetProgress(10, "ЛМ ЧЗ: проверка GUID продукта");
            _log.LogUser(" Проверка ЛМ ЧЗ...");

            string installedGuid = _installer.GetInstalledGuid();
            bool installedByGuid = !string.IsNullOrWhiteSpace(installedGuid);

            if (!installedByGuid)
            {
                SetProgress(20, "ЛМ ЧЗ: чистая установка MSI");
                _log.LogUser("❌ ЛМ ЧЗ не найден по GUID, запускаем чистую установку.");
                await _installer.CleanInstall();
                return await StartApiAndInitialize(token, "после чистой установки");
            }

            SetProgress(20, "ЛМ ЧЗ: продукт найден, проверка API");
            _log.LogUser($"✓ ЛМ ЧЗ найден по GUID: {installedGuid}");

            SetProgress(30, "ЛМ ЧЗ: ожидание статуса API");
            var statusReady = await WaitForConditionAsync(
                condition: IsStatusAvailable,
                conditionName: "получение статуса ЛМ",
                timeoutSeconds: 20,
                initialIntervalMs: 1000,
                maxIntervalMs: 3000,
                progressPercent: 30);

            if (!statusReady)
            {
                SetProgress(40, "ЛМ ЧЗ: API не отвечает, запуск Regime");
                _log.LogUser("⚠️ ЛМ установлен, но API не отвечает. Пробуем запустить службу Regime...");
                if (!await StartServiceAndWaitForApi())
                {
                    _log.LogUser("❌ ЛМ установлен по GUID, но API не поднялся после запуска службы.");
                    return false;
                }
            }

            SetProgress(50, "ЛМ ЧЗ: получение текущего статуса");
            var actualStatus = await GetStatus();
            if (actualStatus == null)
            {
                _log.LogUser("❌ Не удалось получить статус ЛМ после подтверждения установки.");
                return false;
            }

            DetectApiVersion(actualStatus.Version);
            _log.LogUser($"✓ Активен: {actualStatus.Version}, статус: {actualStatus.Status}, ИНН: {MaskInnForUi(actualStatus.Inn)}");

            if (actualStatus.Version != _expectedVersion)
            {
                SetProgress(60, "ЛМ ЧЗ: переустановка нужной версии");
                _log.LogUser($"⚠️ Версия ЛМ {actualStatus.Version} != {_expectedVersion}. Запускаем переустановку старой версии.");
                await _installer.ReinstallExisting($"старая версия {actualStatus.Version}, ожидается {_expectedVersion}");
                return await StartApiAndInitialize(token, "после обновления версии ЛМ");
            }

            if ((actualStatus.Status == "initialization" || actualStatus.Status == "ready")
                && !string.IsNullOrWhiteSpace(expectedInn)
                && !string.IsNullOrWhiteSpace(actualStatus.Inn)
                && actualStatus.Inn != expectedInn)
            {
                _log.LogUser($"⚠️ INN mismatch: в ЛМ {MaskInnForUi(actualStatus.Inn)}, ожидается {MaskInnForUi(expectedInn)}");

                bool shouldReinstall = _dialogService.Confirm(
                    $"ЛМ ЧЗ уже инициализирован на другой ИНН.\n\n" +
                    $"В ЛМ: {actualStatus.Inn}\n" +
                    $"Ожидается: {expectedInn}\n\n" +
                    "Удалить текущий ЛМ ЧЗ и установить заново?",
                    "Конфликт ИНН ЛМ ЧЗ",
                    UserDialogIcon.Warning);

                if (!shouldReinstall)
                {
                    SetProgress(100, "ЛМ ЧЗ: переустановка отменена оператором");
                    _log.LogUser("ℹ️ Пользователь отказался от переустановки ЛМ ЧЗ");
                    return true;
                }

                SetProgress(60, "ЛМ ЧЗ: переустановка из-за другого ИНН");
                await _installer.ReinstallBecauseInnMismatch(actualStatus.Inn, expectedInn);
                return await StartApiAndInitialize(token, "после forced reinstall из-за INN mismatch");
            }

            if (actualStatus.Status == "initialization" || actualStatus.Status == "ready")
            {
                SetProgress(100, "ЛМ ЧЗ: готов");
                _log.LogUser("✅ ЛМ ЧЗ готов");
                return true;
            }

            if (actualStatus.Status == "not_configured")
            {
                SetProgress(70, "ЛМ ЧЗ: требуется init");
                _log.LogUser("⚠️ ЛМ ЧЗ установлен, но не инициализирован. Запускаем init...");
                return await InitializeAndReport(token, "инициализация установленного ЛМ");
            }

            SetProgress(70, $"ЛМ ЧЗ: init при статусе {actualStatus.Status}");
            _log.LogUser($"⚠️ Неожиданный статус ЛМ ЧЗ: {actualStatus.Status}. Пробуем инициализацию.");
            return await InitializeAndReport(token, $"инициализация при статусе {actualStatus.Status}");
        }

        private void DetectApiVersion(string lmVersion)
        {
            if (string.IsNullOrEmpty(lmVersion)) return;

            _apiVersion = lmVersion.StartsWith("1.") ? "v1" : "v2";
            if (_apiVersion == "v1")
                _log.LogUser("ℹ️ Старая версия ЛМ, используем API v1");
        }

        private async Task<bool> WaitForConditionAsync(
            Func<Task<bool>> condition,
            string conditionName,
            int timeoutSeconds = 60,
            int initialIntervalMs = 1000,
            int maxIntervalMs = 5000,
            int progressPercent = 55)
        {
            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            var currentInterval = initialIntervalMs;
            var attempt = 0;

            _log.LogUser($"⏳ Ожидание: {conditionName} (таймаут {timeoutSeconds} сек)");

            while (DateTime.Now - startTime < timeout)
            {
                attempt++;
                SetProgress(progressPercent, $"ЛМ ЧЗ: ожидание {conditionName}, попытка {attempt}");
                try
                {
                    if (await condition())
                    {
                        var elapsed = (DateTime.Now - startTime).TotalSeconds;
                        _log.LogUser($"✅ {conditionName} готов за {elapsed:F1} сек (попытка {attempt})");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _log.LogUser($"⚠️ Попытка {attempt}: {ex.Message}");
                }

                if (attempt < 4)
                {
                    currentInterval = initialIntervalMs * attempt;
                    if (currentInterval > maxIntervalMs)
                        currentInterval = maxIntervalMs;
                }

                await Task.Delay(currentInterval);
            }

            _log.LogUser($"❌ Таймаут {timeoutSeconds} сек: {conditionName} не готов");
            return false;
        }

        private async Task<bool> StartServiceAndWaitForApi()
        {
            SetProgress(45, "ЛМ ЧЗ: запуск службы Regime");
            _log.LogUser(" Запуск службы Regime...");
            await WindowsServiceManager.StartService();

            SetProgress(55, "ЛМ ЧЗ: ожидание API");
            return await WaitForConditionAsync(
                condition: IsApiAvailable,
                conditionName: "API ЛМ ЧЗ",
                timeoutSeconds: 60,
                initialIntervalMs: 2000,
                maxIntervalMs: 5000,
                progressPercent: 55);
        }

        private async Task<bool> IsStatusAvailable()
        {
            var response = await _apiClient.GetStatus();
            return response.IsSuccess && response.Data != null;
        }

        private async Task<bool> StartApiAndInitialize(string token, string context)
        {
            SetProgress(45, $"ЛМ ЧЗ: запуск API {context}");
            _log.LogUser($" Запуск API ЛМ ЧЗ {context}...");
            if (!await StartServiceAndWaitForApi())
                throw new Exception($"API ЛМ ЧЗ не доступен {context}");

            return await InitializeAndReport(token, context);
        }

        private async Task<bool> InitializeAndReport(string token, string context)
        {
            var initResult = await InitializeFull(token);
            if (initResult.IsSuccess)
            {
                SetProgress(100, "ЛМ ЧЗ: инициализация успешна");
                _log.LogUser($"✅ Инициализация ЛМ ЧЗ успешна: {context}");
                return true;
            }

            SetProgress(100, "ЛМ ЧЗ: ошибка инициализации");
            _log.LogUser($"❌ Ошибка инициализации ЛМ ЧЗ ({context}): {initResult.StatusCode} - {initResult.ErrorMessage}");

            string userMessage = initResult.ErrorMessage;
            if (initResult.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                userMessage = "Ошибка авторизации API. Проверьте учётные данные.";
            else if (initResult.StatusCode == System.Net.HttpStatusCode.BadRequest)
                userMessage = "Неверный токен. Проверьте правильность токена ЧЗ.";

            _dialogService.ShowError("Не удалось инициализировать ЛМ ЧЗ:" + Environment.NewLine + userMessage, "Ошибка инициализации");
            return false;
        }

        private static string MaskInnForUi(string inn)
        {
            if (string.IsNullOrWhiteSpace(inn) || inn.Length < 6)
                return inn ?? string.Empty;

            return inn.Substring(0, 4) + new string('*', Math.Max(0, inn.Length - 6)) + inn.Substring(inn.Length - 2);
        }

        private void SetProgress(int lmPercent, string message)
        {
            if (_progress == null)
                return;

            int percent = _progressStart + (_progressEnd - _progressStart) * lmPercent / 100;
            _progress.SetProgress(percent, message);
        }
    }
}
