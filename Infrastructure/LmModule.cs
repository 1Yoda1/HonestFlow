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
        /// Главный метод: проверяет состояние ЛМ, при необходимости устанавливает/обновляет/инициализирует
        /// </summary>
        /// <summary>
        /// Главный метод: проверяет состояние ЛМ, при необходимости устанавливает/обновляет/инициализирует
        /// </summary>
        public async Task<bool> EnsureInstalledAndInitialized(string token, string expectedInn)
        {
            Utils.Log("📦 Проверка ЛМ ЧЗ...");

            // Получаем статус с повторными попытками
            var statusReady = await WaitForConditionAsync(
                condition: IsStatusAvailable,
                conditionName: "получение статуса ЛМ",
                timeoutSeconds: 30,
                initialIntervalMs: 1000,
                maxIntervalMs: 3000
            );

            if (!statusReady)
            {
                Utils.Log("⚠️ Не удалось получить статус ЛМ, продолжаем с установкой...");
            }

            var actualStatus = await GetStatus();

            if (actualStatus != null)
            {
                DetectApiVersion(actualStatus.Version);
                Utils.Log($"✓ Активен: {actualStatus.Version}, статус: {actualStatus.Status}");

                // Версия не совпадает → обновление
                if (actualStatus.Version != _expectedVersion)
                {
                    Utils.Log($"⚠️ Версия {actualStatus.Version} != {_expectedVersion}, обновление...");
                    await _installer.ForceReinstall();

                    if (!await StartServiceAndWaitForApi())
                    {
                        throw new Exception("API не доступен после обновления");
                    }

                    var initResult = await InitializeFull(token);
                    if (!initResult.IsSuccess)
                    {
                        Utils.Log($"❌ Ошибка инициализации: {initResult.StatusCode} - {initResult.ErrorMessage}");
                        return false;
                    }
                    return true;
                }

                // Уже инициализирован или готов
                if (actualStatus.Status == "initialization" || actualStatus.Status == "ready")
                {
                    if (!string.IsNullOrEmpty(expectedInn) && !string.IsNullOrEmpty(actualStatus.Inn) && actualStatus.Inn != expectedInn)
                    {
                        MessageBox.Show(
                            $"ЛМ ЧЗ инициализирован на ИНН {actualStatus.Inn}\nОжидался {expectedInn}\nУдалите вручную и запустите снова.",
                            "Конфликт ИНН", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else
                    {
                        Utils.Log("✅ ЛМ ЧЗ готов");
                    }
                    return true;
                }

                // Не инициализирован → инициализация
                if (actualStatus.Status == "not_configured")
                {
                    Utils.Log("⚠️ Не инициализирован, инициализируем...");
                    var initResult = await InitializeFull(token);
                    if (!initResult.IsSuccess)
                    {
                        Utils.Log($"❌ Ошибка инициализации: {initResult.StatusCode} - {initResult.ErrorMessage}");

                        string userMessage = initResult.ErrorMessage;
                        if (initResult.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                            userMessage = "Ошибка авторизации API. Проверьте учётные данные.";
                        else if (initResult.StatusCode == System.Net.HttpStatusCode.BadRequest)
                            userMessage = "Неверный токен. Проверьте правильность токена ЧЗ.";

                        MessageBox.Show($"Не удалось инициализировать ЛМ ЧЗ:\n{userMessage}",
                            "Ошибка инициализации", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }
                    return true;
                }
            }

            // API не отвечает → проверяем, установлен ли ЛМ
            string path = LmModuleInstaller.FindModulePath();
            if (path != null && File.Exists(path))
            {
                Utils.Log($"✓ Найден по пути {path}, запускаем службу...");

                if (!await StartServiceAndWaitForApi())
                {
                    Utils.Log("❌ Не удалось запустить службу Regime");
                    return false;
                }

                actualStatus = await GetStatus();
                if (actualStatus != null && actualStatus.Status == "not_configured")
                {
                    var initResult = await InitializeFull(token);
                    if (!initResult.IsSuccess)
                    {
                        Utils.Log($"❌ Ошибка инициализации: {initResult.StatusCode} - {initResult.ErrorMessage}");
                        return false;
                    }
                }
                return true;
            }

            // ЛМ не найден → чистая установка
            Utils.Log("❌ ЛМ ЧЗ не найден, чистая установка.");
            await _installer.ForceReinstall();

            if (!await StartServiceAndWaitForApi())
            {
                throw new Exception("API не доступен после установки");
            }

            var finalInit = await InitializeFull(token);
            if (!finalInit.IsSuccess)
            {
                Utils.Log($"❌ Ошибка инициализации после установки: {finalInit.StatusCode} - {finalInit.ErrorMessage}");
                return false;
            }

            return true;
        }
    }
}