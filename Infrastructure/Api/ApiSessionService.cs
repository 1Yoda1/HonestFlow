using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Api
{
    public sealed class ApiSessionService : IApiSessionService
    {
        private readonly HttpClient _httpClient;
        private readonly IApiSessionStore _store;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);

        public ApiSessionService(HttpClient httpClient, IApiSessionStore store)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public async Task<ApiTokenResponse> LoginAsync(string login, string password, string deviceId, string deviceName, CancellationToken cancellationToken)
        {
            var payload = new { login, password, deviceId, deviceName };
            ApiTokenResponse tokens = await PostTokensAsync("api/auth/login", payload, cancellationToken);
            await SaveTokensAsync(tokens, cancellationToken);
            return tokens;
        }

        public async Task<HttpResponseMessage> SendAuthorizedAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ApiSession session = await GetUsableSessionAsync(cancellationToken);
            if (session == null)
                throw new ApiRequestException(HttpStatusCode.Unauthorized);

            HttpResponseMessage response = await SendCloneAsync(request, session.AccessToken, cancellationToken);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
                return response;
            response.Dispose();

            session = await RefreshAsync(session.RefreshToken, cancellationToken);
            if (session == null)
                throw new ApiRequestException(HttpStatusCode.Unauthorized);
            return await SendCloneAsync(request, session.AccessToken, cancellationToken);
        }

        public async Task LogoutAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout");
                using HttpResponseMessage response = await SendAuthorizedAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is ApiRequestException || ex is HttpRequestException ||
                                       ex is TaskCanceledException)
            {
                // Local credentials must be cleared even when the server cannot be reached.
            }
            finally
            {
                await _store.ClearAsync(CancellationToken.None);
            }
        }

        private async Task<ApiSession> GetUsableSessionAsync(CancellationToken cancellationToken)
        {
            ApiSession session = await _store.LoadAsync(cancellationToken);
            if (session == null)
                return null;
            if (session.AccessTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(30))
                return session;
            return await RefreshAsync(session.RefreshToken, cancellationToken);
        }

        private async Task<ApiSession> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        {
            await _refreshLock.WaitAsync(cancellationToken);
            try
            {
                ApiSession current = await _store.LoadAsync(cancellationToken);
                if (current != null && current.AccessTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(30) &&
                    !string.Equals(current.RefreshToken, refreshToken, StringComparison.Ordinal))
                    return current;
                try
                {
                    ApiTokenResponse tokens = await PostTokensAsync("api/auth/refresh", new { refreshToken }, cancellationToken);
                    return await SaveTokensAsync(tokens, cancellationToken);
                }
                catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized || ex.StatusCode == HttpStatusCode.Forbidden)
                {
                    await _store.ClearAsync(cancellationToken);
                    return null;
                }
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private async Task<ApiTokenResponse> PostTokensAsync(string path, object payload, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
            };
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new ApiRequestException(response.StatusCode);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            ApiTokenResponse tokens = JsonConvert.DeserializeObject<ApiTokenResponse>(json);
            if (tokens == null || string.IsNullOrWhiteSpace(tokens.AccessToken) || string.IsNullOrWhiteSpace(tokens.RefreshToken))
                throw new InvalidOperationException("API returned an incomplete token response.");
            return tokens;
        }

        private async Task<ApiSession> SaveTokensAsync(ApiTokenResponse tokens, CancellationToken cancellationToken)
        {
            var session = new ApiSession
            {
                AccessToken = tokens.AccessToken,
                RefreshToken = tokens.RefreshToken,
                AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, tokens.ExpiresInSeconds))
            };
            await _store.SaveAsync(session, cancellationToken);
            return session;
        }

        private async Task<HttpResponseMessage> SendCloneAsync(HttpRequestMessage source, string accessToken, CancellationToken cancellationToken)
        {
            var clone = new HttpRequestMessage(source.Method, source.RequestUri);
            foreach (var header in source.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (source.Content != null)
            {
                byte[] content = await source.Content.ReadAsByteArrayAsync(cancellationToken);
                clone.Content = new ByteArrayContent(content);
                foreach (var header in source.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            clone.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return await _httpClient.SendAsync(clone, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
    }
}
