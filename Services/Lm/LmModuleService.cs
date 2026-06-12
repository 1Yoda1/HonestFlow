using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Api;
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

        public LmModuleService(ILogService log)
        {
            _log = log;
        }

        public LmModuleService(string installerPath, string expectedVersion)
    : this(installerPath, expectedVersion, null)
        {
        }

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

        public LmModuleService(string installerPath, string expectedVersion, ILogService log)
        {
            if (string.IsNullOrWhiteSpace(expectedVersion))
                throw new ArgumentException("Версия ЛМ ЧЗ не задана. Нельзя запускать install/update-сценарий без expectedVersion.", nameof(expectedVersion));

            _apiClient = new LmApiClient(true);
            _installer = new LmModuleInstaller(installerPath);
            _expectedVersion = expectedVersion;
            _log = log;
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
            _log.LogUser($" Инициализация (API {_apiVersion})...");
            return await _apiClient.InitializeFull(token);
        }

        public async Task<bool> EnsureInstalledAndInitialized(string token, string expectedInn)
        {
            using var operation = Logger.BeginOperation("EnsureInstalledAndInitialized ЛМ ЧЗ", nameof(LmModuleService));
            _log.LogUser(" Проверка ЛМ ЧЗ...");

            string installedGuid = _installer.GetInstalledGuid();
            bool installedByGuid = !string.IsNullOrWhiteSpace(installedGuid);

            if (!installedByGuid)
            {
                _log.LogUser("❌ ЛМ ЧЗ не найден по GUID, запускаем чистую установку.");
                await _installer.CleanInstall();
                return await StartApiAndInitialize(token, "после чистой установки");
            }

            _log.LogUser($"✓ ЛМ ЧЗ найден по GUID: {installedGuid}");

            var statusReady = await WaitForConditionAsync(
                condition: IsStatusAvailable,
                conditionName: "получение статуса ЛМ",
                timeoutSeconds: 20,
                initialIntervalMs: 1000,
                maxIntervalMs: 3000);

            if (!statusReady)
            {
                _log.LogUser("⚠️ ЛМ установлен, но API не отвечает. Пробуем запустить службу Regime...");
                if (!await StartServiceAndWaitForApi())
                {
                    _log.LogUser("❌ ЛМ установлен по GUID, но API не поднялся после запуска службы.");
                    return false;
                }
            }

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

                var answer = MessageBox.Show(
                    $"ЛМ ЧЗ уже инициализирован на другой ИНН.\n\n" +
                    $"В ЛМ: {actualStatus.Inn}\n" +
                    $"Ожидается: {expectedInn}\n\n" +
                    "Удалить текущий ЛМ ЧЗ и установить заново?",
                    "Конфликт ИНН ЛМ ЧЗ",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (answer != DialogResult.Yes)
                {
                    _log.LogUser("⚠️ Пользователь отменил переустановку ЛМ ЧЗ из-за конфликта ИНН.");
                    return false;
                }

                await _installer.ReinstallBecauseInnMismatch(actualStatus.Inn, expectedInn);
                return await StartApiAndInitialize(token, "после forced reinstall из-за INN mismatch");
            }

            if (actualStatus.Status == "initialization" || actualStatus.Status == "ready")
            {
                _log.LogUser("✅ ЛМ ЧЗ готов");
                return true;
            }

            if (actualStatus.Status == "not_configured")
            {
                _log.LogUser("⚠️ ЛМ ЧЗ установлен, но не инициализирован. Запускаем init...");
                return await InitializeAndReport(token, "инициализация установленного ЛМ");
            }

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
            int maxIntervalMs = 5000)
        {
            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            var currentInterval = initialIntervalMs;
            var attempt = 0;

            _log.LogUser($"⏳ Ожидание: {conditionName} (таймаут {timeoutSeconds} сек)");

            while (DateTime.Now - startTime < timeout)
            {
                attempt++;
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
            _log.LogUser(" Запуск службы Regime...");
            await WindowsServiceManager.StartService();

            return await WaitForConditionAsync(
                condition: IsApiAvailable,
                conditionName: "API ЛМ ЧЗ",
                timeoutSeconds: 60,
                initialIntervalMs: 2000,
                maxIntervalMs: 5000);
        }

        private async Task<bool> IsStatusAvailable()
        {
            var response = await _apiClient.GetStatus();
            return response.IsSuccess && response.Data != null;
        }

        private async Task<bool> StartApiAndInitialize(string token, string context)
        {
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
                _log.LogUser($"✅ Инициализация ЛМ ЧЗ успешна: {context}");
                return true;
            }

            _log.LogUser($"❌ Ошибка инициализации ЛМ ЧЗ ({context}): {initResult.StatusCode} - {initResult.ErrorMessage}");

            string userMessage = initResult.ErrorMessage;
            if (initResult.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                userMessage = "Ошибка авторизации API. Проверьте учётные данные.";
            else if (initResult.StatusCode == System.Net.HttpStatusCode.BadRequest)
                userMessage = "Неверный токен. Проверьте правильность токена ЧЗ.";

            MessageBox.Show($"Не удалось инициализировать ЛМ ЧЗ:\n{userMessage}", "Ошибка инициализации", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        private static string MaskInnForUi(string inn)
        {
            if (string.IsNullOrWhiteSpace(inn) || inn.Length < 6)
                return inn ?? string.Empty;

            return inn.Substring(0, 4) + new string('*', Math.Max(0, inn.Length - 6)) + inn.Substring(inn.Length - 2);
        }
    }
}
