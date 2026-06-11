using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Infrastructure.Installers;
using HonestFlow.Infrastructure.Services;
using HonestFlow.Models;


namespace HonestFlow.Infrastructure
{
    /// <summary>
    /// Оркестратор установки и инициализации Локального модуля ЧЗ (Regime)
    /// Координирует работу LmApiClient, LmModuleInstaller, RegistryManager, WindowsServiceManager
    /// </summary>
    public class LmModule
    {
        private readonly LmApiClient _apiClient;
        private readonly LmModuleInstaller _installer;
        private readonly string _expectedVersion;
        private string _apiVersion = "v2";

        public LmModule(string installerPath, string expectedVersion)
        {
            _apiClient = new LmApiClient(true); // Включаем детальное логирование
            _installer = new LmModuleInstaller(installerPath);
            _expectedVersion = expectedVersion;
        }

        /// <summary>
        /// Определить версию API по версии модуля
        /// </summary>
        private void DetectApiVersion(string lmVersion)
        {
            if (string.IsNullOrEmpty(lmVersion)) return;
            _apiVersion = lmVersion.StartsWith("1.") ? "v1" : "v2";
            if (_apiVersion == "v1")
                Utils.Log("ℹ️ Старая версия ЛМ, используем API v1");
        }

        /// <summary>
        /// Проверка доступности API (через LmApiClient)
        /// </summary>
        public async Task<bool> IsApiAvailable()
        {
            return await _apiClient.IsApiAvailable();
        }

        /// <summary>
        /// Получение статуса ЛМ ЧЗ (упрощённо, для обратной совместимости)
        /// </summary>
        public async Task<LmStatus> GetStatus()
        {
            var response = await _apiClient.GetStatus();
            return response.IsSuccess ? response.Data : null;
        }

        /// <summary>
        /// Получение статуса с полной информацией (HTTP-статус, ошибки)
        /// </summary>
        public async Task<ApiResponse<LmStatus>> GetStatusFull()
        {
            return await _apiClient.GetStatus();
        }

        /// <summary>
        /// Инициализация ЛМ ЧЗ с токеном (упрощённо, для обратной совместимости)
        /// </summary>
        public async Task<bool> Initialize(string token)
        {
            Utils.Log($"🔑 Инициализация (API {_apiVersion})...");
            return await _apiClient.Initialize(token);
        }

        /// <summary>
        /// Инициализация с детальной обработкой ошибок
        /// </summary>
        public async Task<ApiSimpleResponse> InitializeFull(string token)
        {
            Utils.Log($"🔑 Инициализация (API {_apiVersion})...");
            return await _apiClient.InitializeFull(token);
        }

        /// <summary>
        /// Универсальный метод ожидания условия с прогрессивным интервалом
        /// </summary>
        private static async Task<bool> WaitForConditionAsync(
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

            Utils.Log($"⏳ Ожидание: {conditionName} (таймаут {timeoutSeconds} сек)");

            while (DateTime.Now - startTime < timeout)
            {
                attempt++;
                try
                {
                    if (await condition())
                    {
                        var elapsed = (DateTime.Now - startTime).TotalSeconds;
                        Utils.Log($"✅ {conditionName} готов за {elapsed:F1} сек (попытка {attempt})");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Utils.Log($"⚠️ Попытка {attempt}: {ex.Message}");
                }

                // Прогрессивное увеличение интервала (1, 2, 3, 5, 5... сек)
                if (attempt < 4)
                {
                    currentInterval = initialIntervalMs * attempt;
                    if (currentInterval > maxIntervalMs)
                        currentInterval = maxIntervalMs;
                }

                await Task.Delay(currentInterval);
            }

            Utils.Log($"❌ Таймаут {timeoutSeconds} сек: {conditionName} не готов");
            return false;
        }

        /// <summary>
        /// Запуск службы Regime и ожидание доступности API
        /// </summary>
        private async Task<bool> StartServiceAndWaitForApi()
        {
            Utils.Log("🚀 Запуск службы Regime...");
            await WindowsServiceManager.StartService();

            // Ожидаем API до 60 секунд
            return await WaitForConditionAsync(
                condition: IsApiAvailable,
                conditionName: "API ЛМ ЧЗ",
                timeoutSeconds: 60,
                initialIntervalMs: 2000,
                maxIntervalMs: 5000
            );
        }

        /// <summary>
        /// Проверка доступности статуса с получением объекта
        /// </summary>
        private async Task<bool> IsStatusAvailable()
        {
            var response = await _apiClient.GetStatus();
            return response.IsSuccess && response.Data != null;
        }

        /// <summary>
        /// Главный метод: проверяет состояние ЛМ по GUID, при необходимости устанавливает/обновляет/переинициализирует.
        /// HTTP используется только после того, как понятно физическое состояние продукта в системе.
        /// </summary>
        public async Task<bool> EnsureInstalledAndInitialized(string token, string expectedInn)
        {
            using var operation = Logger.BeginOperation("EnsureInstalledAndInitialized ЛМ ЧЗ", nameof(LmModule));

            Utils.Log("📦 Проверка ЛМ ЧЗ...");

            string installedGuid = _installer.GetInstalledGuid();
            bool installedByGuid = !string.IsNullOrWhiteSpace(installedGuid);

            if (!installedByGuid)
            {
                Utils.Log("❌ ЛМ ЧЗ не найден по GUID, запускаем чистую установку.");
                await _installer.CleanInstall();
                return await StartApiAndInitialize(token, "после чистой установки");
            }

            Utils.Log($"✓ ЛМ ЧЗ найден по GUID: {installedGuid}");

            // Если продукт установлен, но API не отвечает — сначала пробуем поднять службу.
            var statusReady = await WaitForConditionAsync(
                condition: IsStatusAvailable,
                conditionName: "получение статуса ЛМ",
                timeoutSeconds: 20,
                initialIntervalMs: 1000,
                maxIntervalMs: 3000
            );

            if (!statusReady)
            {
                Utils.Log("⚠️ ЛМ установлен, но API не отвечает. Пробуем запустить службу Regime...");

                if (!await StartServiceAndWaitForApi())
                {
                    Utils.Log("❌ ЛМ установлен по GUID, но API не поднялся после запуска службы.");
                    return false;
                }
            }

            var actualStatus = await GetStatus();
            if (actualStatus == null)
            {
                Utils.Log("❌ Не удалось получить статус ЛМ после подтверждения установки.");
                return false;
            }

            DetectApiVersion(actualStatus.Version);
            Utils.Log($"✓ Активен: {actualStatus.Version}, статус: {actualStatus.Status}, ИНН: {MaskInnForUi(actualStatus.Inn)}");

            if (actualStatus.Version != _expectedVersion)
            {
                Utils.Log($"⚠️ Версия ЛМ {actualStatus.Version} != {_expectedVersion}. Запускаем переустановку старой версии.");
                await _installer.ReinstallExisting($"старая версия {actualStatus.Version}, ожидается {_expectedVersion}");
                return await StartApiAndInitialize(token, "после обновления версии ЛМ");
            }

            if ((actualStatus.Status == "initialization" || actualStatus.Status == "ready") &&
                !string.IsNullOrWhiteSpace(expectedInn) &&
                !string.IsNullOrWhiteSpace(actualStatus.Inn) &&
                actualStatus.Inn != expectedInn)
            {
                Utils.Log($"⚠️ INN mismatch: в ЛМ {MaskInnForUi(actualStatus.Inn)}, ожидается {MaskInnForUi(expectedInn)}");

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
                    Utils.Log("⚠️ Пользователь отменил переустановку ЛМ ЧЗ из-за конфликта ИНН.");
                    return false;
                }

                await _installer.ReinstallBecauseInnMismatch(actualStatus.Inn, expectedInn);
                return await StartApiAndInitialize(token, "после forced reinstall из-за INN mismatch");
            }

            if (actualStatus.Status == "initialization" || actualStatus.Status == "ready")
            {
                Utils.Log("✅ ЛМ ЧЗ готов");
                return true;
            }

            if (actualStatus.Status == "not_configured")
            {
                Utils.Log("⚠️ ЛМ ЧЗ установлен, но не инициализирован. Запускаем init...");
                return await InitializeAndReport(token, "инициализация установленного ЛМ");
            }

            Utils.Log($"⚠️ Неожиданный статус ЛМ ЧЗ: {actualStatus.Status}. Пробуем инициализацию.");
            return await InitializeAndReport(token, $"инициализация при статусе {actualStatus.Status}");
        }

        private async Task<bool> StartApiAndInitialize(string token, string context)
        {
            Utils.Log($"🚀 Запуск API ЛМ ЧЗ {context}...");

            if (!await StartServiceAndWaitForApi())
            {
                throw new Exception($"API ЛМ ЧЗ не доступен {context}");
            }

            return await InitializeAndReport(token, context);
        }

        private async Task<bool> InitializeAndReport(string token, string context)
        {
            var initResult = await InitializeFull(token);
            if (initResult.IsSuccess)
            {
                Utils.Log($"✅ Инициализация ЛМ ЧЗ успешна: {context}");
                return true;
            }

            Utils.Log($"❌ Ошибка инициализации ЛМ ЧЗ ({context}): {initResult.StatusCode} - {initResult.ErrorMessage}");

            string userMessage = initResult.ErrorMessage;
            if (initResult.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                userMessage = "Ошибка авторизации API. Проверьте учётные данные.";
            else if (initResult.StatusCode == System.Net.HttpStatusCode.BadRequest)
                userMessage = "Неверный токен. Проверьте правильность токена ЧЗ.";

            MessageBox.Show($"Не удалось инициализировать ЛМ ЧЗ:\n{userMessage}",
                "Ошибка инициализации", MessageBoxButtons.OK, MessageBoxIcon.Error);

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