using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using HonestFlow.Models;

namespace HonestFlow.Infrastructure.Api
{
    public class LmApiClient : IDisposable
    {
        private HttpClient _client;
        private string _apiVersion = "v2";
        private readonly bool _enableDetailedLogging;
        private bool _isDisposed = false;
        private readonly object _lockObject = new();

        public LmApiClient(bool enableDetailedLogging = true)
        {
            _enableDetailedLogging = enableDetailedLogging;
            EnsureClientCreated();
        }

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
                        Logger.LogToFile("[DEBUG] HttpClient создан", false);
                }
            }
        }

        private string GetApiUrl(string endpoint) => $"http://127.0.0.1:5995/api/{_apiVersion}/{endpoint}";
        private static string GetAuthHeader() => Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:admin"));

        private void LogRequest(string method, string url, string requestBody = null)
        {
            if (!_enableDetailedLogging) return;

            Logger.LogToFile($"┌───────────── HTTP ЗАПРОС ─────────────", false);
            Logger.LogToFile($"│ {method} {url}", false);
            if (!string.IsNullOrEmpty(requestBody))
            {
                var truncated = requestBody.Length > 500 ? string.Concat(requestBody.AsSpan(0, 500), "...") : requestBody;
                Logger.LogToFile($"│ Body: {truncated}", false);
            }
        }

        private void LogResponse(HttpStatusCode statusCode, string responseBody, double durationMs, bool isError = false)
        {
            if (!_enableDetailedLogging) return;

            var statusIcon = isError ? "❌" : (int)statusCode < 400 ? "✅" : "⚠️";
            Logger.LogToFile($"│ {statusIcon} Ответ: {(int)statusCode} {statusCode} ({durationMs:F0} мс)", false);

            if (!string.IsNullOrEmpty(responseBody) && _enableDetailedLogging)
            {
                var truncated = responseBody.Length > 500 ? string.Concat(responseBody.AsSpan(0, 500), "...") : responseBody;
                Logger.LogToFile($"│ Body: {truncated}", false);
            }
            Logger.LogToFile($"└─────────────────────────────────────────", false);
        }

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
                    try
                    {
                        var data = JsonConvert.DeserializeObject<T>(responseBody);
                        return ApiResponse<T>.Success(data, resp.StatusCode, responseBody, durationMs);
                    }
                    catch (JsonException jsonEx)
                    {
                        // ❌ Повреждённый JSON
                        Logger.LogToFile($"❌ JSON повреждён: {jsonEx.Message}", true);
                        Logger.LogToFile($"   Получено: {responseBody?.Substring(0, Math.Min(500, responseBody?.Length ?? 0))}", true);

                        return ApiResponse<T>.Failure(
                            HttpStatusCode.InternalServerError,
                            $"Ошибка парсинга JSON: {jsonEx.Message}",
                            responseBody,
                            durationMs);
                    }
                }

                return ApiResponse<T>.Failure(resp.StatusCode, $"HTTP {(int)resp.StatusCode}", responseBody, durationMs);
            }
            catch (JsonException jsonEx)
            {
                // Глобальный перехват на всякий случай
                var durationMs = stopwatch.Elapsed.TotalMilliseconds;
                Logger.LogToFile($"❌ JSON исключение: {jsonEx.Message}", true);
                return ApiResponse<T>.Failure(HttpStatusCode.InternalServerError, $"JSON ошибка: {jsonEx.Message}", null, durationMs);
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
                    if (result.Data.Version != null && result.Data.Version.StartsWith("1."))
                        _apiVersion = "v1";

                    return result;
                }

                _apiVersion = originalVersion;
            }

            return ApiResponse<LmStatus>.Failure(HttpStatusCode.ServiceUnavailable, "API не доступен", null, 0);
        }

        public async Task<LmStatus> GetStatusSimple()
        {
            var response = await GetStatus();
            return response.IsSuccess ? response.Data : null;
        }

        public async Task<ApiSimpleResponse> InitializeFull(string token)
        {
            EnsureClientCreated();

            var data = new { token };
            return await PostAsync("init", data);
        }

        public async Task<bool> Initialize(string token)
        {
            var response = await InitializeFull(token);
            return response.IsSuccess;
        }

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
                        Logger.LogToFile("[DEBUG] HttpClient уничтожен", false);
                }
            }
        }
    }
}