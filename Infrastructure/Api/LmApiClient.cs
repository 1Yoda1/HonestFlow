using HonestFlow.Models;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace HonestFlow.Infrastructure.Api
{
    /// <summary>
    /// Клиент для работы с API Локального модуля ЧЗ (порт 5995)
    /// </summary>
    public class LmApiClient : IDisposable
    {
        private HttpClient _client;
        private string _apiVersion = "v2";
        private readonly bool _enableDetailedLogging;
        private bool _isDisposed = false;
        private readonly object _lockObject = new();

        /// <summary>
        /// Создаёт экземпляр клиента
        /// </summary>
        /// <param name="enableDetailedLogging">Включить детальное логирование всех запросов</param>
        public LmApiClient(bool enableDetailedLogging = true)
        {
            _enableDetailedLogging = enableDetailedLogging;
            EnsureClientCreated();
        }

        /// <summary>
        /// Гарантирует, что HttpClient создан и не уничтожен
        /// </summary>
        private void EnsureClientCreated()
        {
            lock (_lockObject)
            {
                if (_client == null || _isDisposed)
                {
                    _client = new HttpClient
                    {
                        Timeout = TimeSpan.FromSeconds(10)
                    };
                    _isDisposed = false;

                    if (_enableDetailedLogging)
                        Logger.LogToFile("[DEBUG] HttpClient создан", true);
                }
            }
        }

        private string GetApiUrl(string endpoint) => $"http://127.0.0.1:5995/api/{_apiVersion}/{endpoint}";
        private static string GetAuthHeader() => Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:admin"));

        /// <summary>
        /// Логирование запроса с деталями
        /// </summary>
        private void LogRequest(string method, string url, string requestBody = null)
        {
            if (!_enableDetailedLogging) return;

            Logger.LogToFile($"┌───────────── HTTP ЗАПРОС ─────────────", true);
            Logger.LogToFile($"│ {method} {url}", true);
            if (!string.IsNullOrEmpty(requestBody))
            {
                var truncated = requestBody.Length > 500 ? string.Concat(requestBody.AsSpan(0, 500), "...") : requestBody;
                Logger.LogToFile($"│ Body: {truncated}", true);
            }
        }

        /// <summary>
        /// Логирование ответа с деталями
        /// </summary>
        private void LogResponse(HttpStatusCode statusCode, string responseBody, double durationMs, bool isError = false)
        {
            if (!_enableDetailedLogging) return;

            var statusIcon = isError ? "❌" : (int)statusCode < 400 ? "✅" : "⚠️";
            Logger.LogToFile($"│ {statusIcon} Ответ: {(int)statusCode} {statusCode} ({durationMs:F0} мс)", true);

            if (!string.IsNullOrEmpty(responseBody) && _enableDetailedLogging)
            {
                var truncated = responseBody.Length > 500 ? string.Concat(responseBody.AsSpan(0, 500), "...") : responseBody;
                Logger.LogToFile($"│ Body: {truncated}", true);
            }
            Logger.LogToFile($"└─────────────────────────────────────────", true);
        }

        /// <summary>
        /// Выполнить GET-запрос с обработкой ответа
        /// </summary>
        private async Task<ApiResponse<T>> GetAsync<T>(string endpoint)
        {
            EnsureClientCreated();

            var url = GetApiUrl(endpoint);
            var stopwatch = Stopwatch.StartNew();

            LogRequest("GET", url);

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", GetAuthHeader());

                using var resp = await _client.SendAsync(req);
                var durationMs = stopwatch.Elapsed.TotalMilliseconds;
                var responseBody = await resp.Content.ReadAsStringAsync();

                LogResponse(resp.StatusCode, responseBody, durationMs, !resp.IsSuccessStatusCode);

                if (resp.IsSuccessStatusCode)
                {
                    var data = JsonConvert.DeserializeObject<T>(responseBody);
                    return ApiResponse<T>.Success(data, resp.StatusCode, responseBody, durationMs);
                }

                return ApiResponse<T>.Failure(resp.StatusCode, $"HTTP {(int)resp.StatusCode}", responseBody, durationMs);
            }
            catch (ObjectDisposedException)
            {
                var durationMs = stopwatch.Elapsed.TotalMilliseconds;
                Logger.LogToFile($"│ ❌ HttpClient уничтожен, пересоздаём...", true);
                Logger.LogToFile($"└─────────────────────────────────────────", true);

                _isDisposed = true;
                EnsureClientCreated();

                return await GetAsync<T>(endpoint);
            }
            catch (Exception ex)
            {
                var durationMs = stopwatch.Elapsed.TotalMilliseconds;
                Logger.LogToFile($"│ ❌ ИСКЛЮЧЕНИЕ: {ex.Message}", true);
                Logger.LogToFile($"└─────────────────────────────────────────", true);

                return ApiResponse<T>.Failure(HttpStatusCode.ServiceUnavailable, ex.Message, null, durationMs);
            }
        }

        /// <summary>
        /// Выполнить POST-запрос с обработкой ответа
        /// </summary>
        private async Task<ApiSimpleResponse> PostAsync(string endpoint, object data)
        {
            EnsureClientCreated();

            var url = GetApiUrl(endpoint);
            var json = JsonConvert.SerializeObject(data);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var stopwatch = Stopwatch.StartNew();

            LogRequest("POST", url, json);

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", GetAuthHeader());
                req.Content = content;

                using var resp = await _client.SendAsync(req);
                var durationMs = stopwatch.Elapsed.TotalMilliseconds;
                var responseBody = await resp.Content.ReadAsStringAsync();

                LogResponse(resp.StatusCode, responseBody, durationMs, !resp.IsSuccessStatusCode);

                if (resp.IsSuccessStatusCode)
                {
                    return ApiSimpleResponse.Success(resp.StatusCode, responseBody, durationMs);
                }

                string errorMessage = $"HTTP {(int)resp.StatusCode}";
                try
                {
                    var errorObj = JsonConvert.DeserializeObject<dynamic>(responseBody);
                    if (errorObj?.message != null)
                        errorMessage = errorObj.message.ToString();
                }
                catch { }

                return ApiSimpleResponse.Failure(resp.StatusCode, errorMessage, responseBody, durationMs);
            }
            catch (ObjectDisposedException)
            {
                var durationMs = stopwatch.Elapsed.TotalMilliseconds;
                Logger.LogToFile($"│ ❌ HttpClient уничтожен, пересоздаём...", true);
                Logger.LogToFile($"└─────────────────────────────────────────", true);

                _isDisposed = true;
                EnsureClientCreated();

                return await PostAsync(endpoint, data);
            }
            catch (Exception ex)
            {
                var durationMs = stopwatch.Elapsed.TotalMilliseconds;
                Logger.LogToFile($"│ ❌ ИСКЛЮЧЕНИЕ: {ex.Message}", true);
                Logger.LogToFile($"└─────────────────────────────────────────", true);

                return ApiSimpleResponse.Failure(HttpStatusCode.ServiceUnavailable, ex.Message, null, durationMs);
            }
        }

        /// <summary>
        /// Проверить доступность API (перебором v2 и v1)
        /// </summary>
        public async Task<bool> IsApiAvailable()
        {
            EnsureClientCreated();

            string[] versions = { "v2", "v1" };
            foreach (var v in versions)
            {
                var url = $"http://127.0.0.1:5995/api/{v}/status";

                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", GetAuthHeader());

                    using var resp = await _client.SendAsync(req);
                    if (resp.IsSuccessStatusCode)
                    {
                        _apiVersion = v;
                        return true;
                    }
                }
                catch (ObjectDisposedException)
                {
                    _isDisposed = true;
                    EnsureClientCreated();
                    return await IsApiAvailable();
                }
                catch (Exception ex)
                {
                    Logger.LogToFile($"IsApiAvailable ошибка для версии {v}: {ex.Message}", true);
                }
            }
            return false;
        }

        /// <summary>
        /// Получить статус ЛМ ЧЗ (версия, статус, ИНН)
        /// </summary>
        public async Task<ApiResponse<LmStatus>> GetStatus()
        {
            EnsureClientCreated();

            string[] versions = { _apiVersion, "v2", "v1" };
            var triedVersions = new System.Collections.Generic.HashSet<string>();

            foreach (var v in versions)
            {
                if (triedVersions.Contains(v)) continue;
                triedVersions.Add(v);

                var originalVersion = _apiVersion;
                _apiVersion = v;

                var result = await GetAsync<LmStatus>("status");

                if (result.IsSuccess && result.Data != null)
                {
                    if (result.Data.version != null && result.Data.version.StartsWith("1."))
                        _apiVersion = "v1";

                    return result;
                }

                _apiVersion = originalVersion;
            }

            return ApiResponse<LmStatus>.Failure(HttpStatusCode.ServiceUnavailable, "API не доступен", null, 0);
        }

        /// <summary>
        /// Получить статус (упрощённо, без ApiResponse)
        /// </summary>
        public async Task<LmStatus> GetStatusSimple()
        {
            var response = await GetStatus();
            return response.IsSuccess ? response.Data : null;
        }

        /// <summary>
        /// Ожидание доступности API с таймаутом
        /// </summary>
        private async Task<bool> WaitForApiAvailable(int timeoutSeconds = 30)
        {
            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);

            while (DateTime.Now - startTime < timeout)
            {
                if (await IsApiAvailable())
                    return true;

                await Task.Delay(1000);
            }

            Logger.LogToFile($"WaitForApiAvailable: таймаут {timeoutSeconds} сек", true);
            return false;
        }

        /// <summary>
        /// Инициализация ЛМ ЧЗ с переданным токеном (с полной информацией об ошибке)
        /// </summary>
        public async Task<ApiSimpleResponse> InitializeFull(string token)
        {
            EnsureClientCreated();

            if (!await WaitForApiAvailable(30))
            {
                Logger.LogToFile("InitializeFull: API не доступен после ожидания", true);
                return ApiSimpleResponse.Failure(HttpStatusCode.ServiceUnavailable, "API не доступен", null, 0);
            }

            var data = new { token };
            return await PostAsync("init", data);
        }

        /// <summary>
        /// Инициализация (простая, для обратной совместимости)
        /// </summary>
        public async Task<bool> Initialize(string token)
        {
            var response = await InitializeFull(token);

            if (!response.IsSuccess)
            {
                Logger.LogToFile($"Initialize ошибка: {response.StatusCode} - {response.ErrorMessage}", true);
            }

            return response.IsSuccess;
        }

        /// <summary>
        /// Освобождение ресурсов
        /// </summary>
        public void Dispose()
        {
            lock (_lockObject)
            {
                if (!_isDisposed && _client != null)
                {
                    _client.Dispose();
                    _client = null;
                    _isDisposed = true;

                    if (_enableDetailedLogging)
                        Logger.LogToFile("[DEBUG] HttpClient уничтожен", true);
                }
            }

            // Подавляем финализацию, так как все ресурсы уже освобождены
            GC.SuppressFinalize(this);
        }
    }
}