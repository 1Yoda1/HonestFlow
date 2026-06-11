using System;
using System.Collections.Generic;
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
        private const string ModuleName = nameof(LmApiClient);

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
                    _client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                    _isDisposed = false;

                    if (_enableDetailedLogging)
                        Logger.DebugLog("HttpClient создан", ModuleName);
                }
            }
        }

        private string GetApiUrl(string endpoint) => $"http://127.0.0.1:5995/api/{_apiVersion}/{endpoint}";

        private static string GetAuthHeader() => Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:admin"));

        private void LogRequest(string method, string url, string requestBody = null)
        {
            if (!_enableDetailedLogging)
                return;

            Logger.DebugLog($"HTTP {method} {url}" + (string.IsNullOrWhiteSpace(requestBody) ? string.Empty : $" | Body: {requestBody}"), "HTTP");
        }

        private void LogResponse(string method, string url, HttpStatusCode statusCode, string responseBody, double durationMs, bool isError = false)
        {
            if (!_enableDetailedLogging)
                return;

            Logger.LogHttp(method, url, (int)statusCode, statusCode.ToString(), durationMs, isError, responseBody);
        }

        private async Task<ApiResponse<T>> GetAsync<T>(string endpoint, bool detailedRequestLogging = true)
        {
            EnsureClientCreated();
            var url = GetApiUrl(endpoint);
            var stopwatch = Stopwatch.StartNew();

            if (detailedRequestLogging)
                LogRequest("GET", url);

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", GetAuthHeader());

                using var resp = await _client.SendAsync(req);
                var durationMs = stopwatch.Elapsed.TotalMilliseconds;
                var responseBody = await resp.Content.ReadAsStringAsync();

                if (detailedRequestLogging)
                    LogResponse("GET", url, resp.StatusCode, responseBody, durationMs, !resp.IsSuccessStatusCode);

                if (resp.IsSuccessStatusCode)
                {
                    try
                    {
                        var data = JsonConvert.DeserializeObject<T>(responseBody);
                        return ApiResponse<T>.Success(data, resp.StatusCode, responseBody, durationMs);
                    }
                    catch (JsonException jsonEx)
                    {
                        Logger.Error($"JSON повреждён: {jsonEx.Message} | Получено: {Truncate(responseBody, 500)}", ModuleName);
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
                var durationMs = stopwatch.Elapsed.TotalMilliseconds;
                Logger.Error($"JSON исключение: {jsonEx.Message}", ModuleName);
                return ApiResponse<T>.Failure(HttpStatusCode.InternalServerError, $"JSON ошибка: {jsonEx.Message}", null, durationMs);
            }
            catch (ObjectDisposedException)
            {
                _isDisposed = true;
                EnsureClientCreated();
                Logger.Warning("HttpClient был уничтожен, пересоздан. Повторяем GET", ModuleName);
                return await GetAsync<T>(endpoint, detailedRequestLogging);
            }
            catch (Exception ex)
            {
                var durationMs = stopwatch.Elapsed.TotalMilliseconds;

                if (detailedRequestLogging)
                    Logger.Error($"HTTP GET {url} -> исключение за {durationMs:F0} мс: {ex.Message}", "HTTP");

                return ApiResponse<T>.Failure(HttpStatusCode.ServiceUnavailable, ex.Message, null, durationMs);
            }
        }

        private async Task<ApiSimpleResponse> PostAsync(string endpoint, object data)
        {
            EnsureClientCreated();
            var url = GetApiUrl(endpoint);
            var json = JsonConvert.SerializeObject(data);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
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

                LogResponse("POST", url, resp.StatusCode, responseBody, durationMs, !resp.IsSuccessStatusCode);

                if (resp.IsSuccessStatusCode)
                    return ApiSimpleResponse.Success(resp.StatusCode, responseBody, durationMs);

                string errorMessage = $"HTTP {(int)resp.StatusCode}";
                try
                {
                    dynamic errorObj = JsonConvert.DeserializeObject(responseBody);
                    if (errorObj?.message != null)
                        errorMessage = errorObj.message.ToString();
                }
                catch
                {
                    // Не смогли разобрать тело ошибки — оставляем HTTP-код.
                }

                return ApiSimpleResponse.Failure(resp.StatusCode, errorMessage, responseBody, durationMs);
            }
            catch (ObjectDisposedException)
            {
                _isDisposed = true;
                EnsureClientCreated();
                Logger.Warning("HttpClient был уничтожен, пересоздан. Повторяем POST", ModuleName);
                return await PostAsync(endpoint, data);
            }
            catch (Exception ex)
            {
                var durationMs = stopwatch.Elapsed.TotalMilliseconds;
                Logger.Error($"HTTP POST {url} -> исключение за {durationMs:F0} мс: {ex.Message}", "HTTP");
                return ApiSimpleResponse.Failure(HttpStatusCode.ServiceUnavailable, ex.Message, null, durationMs);
            }
        }

        public async Task<bool> IsApiAvailable()
        {
            EnsureClientCreated();
            string[] versions = { "v2", "v1" };
            var failedVersions = new List<string>();
            var stopwatch = Stopwatch.StartNew();

            foreach (var version in versions)
            {
                var url = $"http://127.0.0.1:5995/api/{version}/status";

                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", GetAuthHeader());

                    using var resp = await _client.SendAsync(req);
                    if (resp.IsSuccessStatusCode)
                    {
                        _apiVersion = version;
                        Logger.DebugLog($"API ЛМ доступен: {version} ({stopwatch.Elapsed.TotalMilliseconds:F0} мс)", ModuleName);
                        return true;
                    }

                    failedVersions.Add($"{version}: HTTP {(int)resp.StatusCode}");
                }
                catch (ObjectDisposedException)
                {
                    _isDisposed = true;
                    EnsureClientCreated();
                    Logger.Warning("HttpClient был уничтожен, пересоздан. Повторяем проверку API", ModuleName);
                    return await IsApiAvailable();
                }
                catch (Exception ex)
                {
                    failedVersions.Add($"{version}: {ex.Message}");
                }
            }

            Logger.LogHttpPolling($"API ЛМ пока недоступен ({string.Join("; ", failedVersions)})", ModuleName);
            return false;
        }

        public async Task<ApiResponse<LmStatus>> GetStatus()
        {
            EnsureClientCreated();
            string[] versions = { _apiVersion, "v2", "v1" };
            var triedVersions = new HashSet<string>();
            var failures = new List<string>();

            foreach (var version in versions)
            {
                if (triedVersions.Contains(version))
                    continue;

                triedVersions.Add(version);
                var originalVersion = _apiVersion;
                _apiVersion = version;

                bool detailed = failures.Count == 0;
                var result = await GetAsync<LmStatus>("status", detailedRequestLogging: detailed);
                if (result.IsSuccess && result.Data != null)
                {
                    if (result.Data.Version != null && result.Data.Version.StartsWith("1."))
                        _apiVersion = "v1";

                    return result;
                }

                failures.Add($"{version}: {result.ErrorMessage}");
                _apiVersion = originalVersion;
            }

            Logger.DebugLog($"Статус ЛМ не получен ({string.Join("; ", failures)})", ModuleName);
            return ApiResponse<LmStatus>.Failure(HttpStatusCode.ServiceUnavailable, "API не доступен", null, 0);
        }

        public async Task<ApiSimpleResponse> InitializeFull(string token)
        {
            EnsureClientCreated();
            var data = new { token };
            return await PostAsync("init", data);
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
                        Logger.DebugLog("HttpClient уничтожен", ModuleName);
                }
            }
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;

            return text.Substring(0, maxLength) + "...";
        }
    }
}
