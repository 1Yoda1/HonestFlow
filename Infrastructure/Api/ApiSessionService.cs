using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Auth;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Api
{
    public sealed class ApiSessionService :
        IApiSessionService,
        IApiSessionRefresher,
        IApiSessionPersistenceController,
        IApiClientAccessStateProvider
    {
        private readonly HttpClient _httpClient;
        private readonly IApiSessionStore _store;
        private readonly IApiSessionStore _registrationStore;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private ApiSession _processSession;
        private bool _persistSession = true;
        private SessionPersistenceMode _persistenceMode = SessionPersistenceMode.Remembered;

        public ApiSessionService(HttpClient httpClient, IApiSessionStore store,
            IApiSessionStore registrationStore = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _registrationStore = registrationStore ?? new NullApiSessionStore();
        }

        public void SetPersistSession(bool persistSession)
        {
            _persistSession = persistSession;
        }

        public bool? LicensePolicyEnabled => _processSession?.LicensePolicyEnabled;
        public string ClientId => _processSession?.ClientId;
        public string ClientName => _processSession?.ClientName;

        public async Task<ApiTokenResponse> LoginAsync(string login, string password, string deviceId, string deviceName, CancellationToken cancellationToken)
        {
            var payload = new { password, deviceId, deviceName };
            ApiTokenResponse tokens = await PostTokensAsync("api/auth/login", payload, cancellationToken);
            _persistenceMode = tokens.DeviceRegistrationRequired
                ? SessionPersistenceMode.ProcessOnly
                : _persistSession ? SessionPersistenceMode.Remembered : SessionPersistenceMode.ProcessOnly;
            await SaveTokensAsync(tokens, deviceId, cancellationToken);
            return tokens;
        }

        public async Task<ApiSession> RestoreRegistrationContinuationAsync(CancellationToken cancellationToken)
        {
            if (_processSession != null)
                return null;

            ApiSession continuation = await _registrationStore.LoadAsync(cancellationToken);
            if (continuation == null || string.IsNullOrWhiteSpace(continuation.ExternalDeviceId))
                return null;

            _processSession = continuation;
            _persistSession = continuation.RememberActiveSession;
            _persistenceMode = SessionPersistenceMode.RegistrationContinuation;
            return continuation;
        }

        public async Task PersistRegistrationContinuationAsync(CancellationToken cancellationToken)
        {
            ApiSession session = await LoadCurrentSessionAsync(cancellationToken);
            if (session == null)
                throw new InvalidOperationException("A restricted API session is required for registration continuation.");

            session.RememberActiveSession = _persistSession;
            _persistenceMode = SessionPersistenceMode.RegistrationContinuation;
            await _registrationStore.SaveAsync(session, cancellationToken);
            await _store.ClearAsync(CancellationToken.None);
        }

        public Task ClearRegistrationContinuationAsync(CancellationToken cancellationToken) =>
            _registrationStore.ClearAsync(cancellationToken);

        public void PrepareForRegistrationCompletion() =>
            _persistenceMode = SessionPersistenceMode.CompletingRegistration;

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
                await ClearSessionAsync(CancellationToken.None);
            }
        }

        public async Task<bool> RefreshSessionAsync(CancellationToken cancellationToken)
        {
            ApiSession session = await LoadCurrentSessionAsync(cancellationToken);
            if (session == null || string.IsNullOrWhiteSpace(session.RefreshToken))
                return false;
            return await RefreshAsync(session.RefreshToken, cancellationToken) != null;
        }

        private async Task<ApiSession> GetUsableSessionAsync(CancellationToken cancellationToken)
        {
            ApiSession session = await LoadCurrentSessionAsync(cancellationToken);
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
                ApiSession current = await LoadCurrentSessionAsync(cancellationToken);
                if (current != null && current.AccessTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(30) &&
                    !string.Equals(current.RefreshToken, refreshToken, StringComparison.Ordinal))
                    return current;
                try
                {
                    ApiTokenResponse tokens = await PostTokensAsync("api/auth/refresh", new { refreshToken }, cancellationToken);
                    return await SaveTokensAsync(tokens, current?.ExternalDeviceId, cancellationToken);
                }
                catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized || ex.StatusCode == HttpStatusCode.Forbidden)
                {
                    await ClearSessionAsync(cancellationToken);
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
            {
                string errorCode = null;
                try
                {
                    string errorJson = await response.Content.ReadAsStringAsync(cancellationToken);
                    errorCode = JsonConvert.DeserializeObject<ApiErrorResponse>(errorJson)?.Code;
                }
                catch (JsonException)
                {
                    // Preserve the HTTP failure even if an older server returned a non-JSON body.
                }
                throw new ApiRequestException(response.StatusCode, errorCode);
            }
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            ApiTokenResponse tokens = JsonConvert.DeserializeObject<ApiTokenResponse>(json);
            if (tokens == null || string.IsNullOrWhiteSpace(tokens.AccessToken) || string.IsNullOrWhiteSpace(tokens.RefreshToken))
                throw new InvalidOperationException("API returned an incomplete token response.");
            return tokens;
        }

        private async Task<ApiSession> SaveTokensAsync(
            ApiTokenResponse tokens, string externalDeviceId, CancellationToken cancellationToken)
        {
            var session = new ApiSession
            {
                AccessToken = tokens.AccessToken,
                RefreshToken = tokens.RefreshToken,
                AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, tokens.ExpiresInSeconds)),
                ClientId = tokens.ClientId,
                ClientName = tokens.ClientName,
                LicensePolicyEnabled = tokens.LicensePolicyEnabled,
                ExternalDeviceId = externalDeviceId ?? _processSession?.ExternalDeviceId,
                RememberActiveSession = _persistSession
            };
            _processSession = session;
            switch (_persistenceMode)
            {
                case SessionPersistenceMode.RegistrationContinuation:
                    await _registrationStore.SaveAsync(session, cancellationToken);
                    await _store.ClearAsync(CancellationToken.None);
                    break;

                case SessionPersistenceMode.CompletingRegistration:
                    if (tokens.DeviceRegistrationRequired)
                    {
                        _persistenceMode = SessionPersistenceMode.RegistrationContinuation;
                        await _registrationStore.SaveAsync(session, cancellationToken);
                        await _store.ClearAsync(CancellationToken.None);
                    }
                    else if (_persistSession)
                    {
                        _persistenceMode = SessionPersistenceMode.Remembered;
                        await _store.SaveAsync(session, cancellationToken);
                        await _registrationStore.ClearAsync(CancellationToken.None);
                    }
                    else
                    {
                        _persistenceMode = SessionPersistenceMode.ProcessOnly;
                        await _store.ClearAsync(CancellationToken.None);
                        await _registrationStore.ClearAsync(CancellationToken.None);
                    }
                    break;

                case SessionPersistenceMode.Remembered:
                    await _store.SaveAsync(session, cancellationToken);
                    await _registrationStore.ClearAsync(CancellationToken.None);
                    break;

                default:
                    // A previous remembered login or continuation must not survive
                    // a successful process-only login.
                    await _store.ClearAsync(CancellationToken.None);
                    await _registrationStore.ClearAsync(CancellationToken.None);
                    break;
            }
            return session;
        }

        private async Task<ApiSession> LoadCurrentSessionAsync(CancellationToken cancellationToken)
        {
            if (_processSession != null)
                return _processSession;

            ApiSession persisted = await _store.LoadAsync(cancellationToken);
            if (persisted != null)
            {
                _processSession = persisted;
                _persistSession = true;
                _persistenceMode = SessionPersistenceMode.Remembered;
            }
            return persisted;
        }

        private async Task ClearSessionAsync(CancellationToken cancellationToken)
        {
            _processSession = null;
            await _store.ClearAsync(cancellationToken);
            await _registrationStore.ClearAsync(cancellationToken);
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

        private sealed class ApiErrorResponse
        {
            public string Code { get; set; }
        }

        private enum SessionPersistenceMode
        {
            ProcessOnly,
            RegistrationContinuation,
            Remembered,
            CompletingRegistration
        }

        private sealed class NullApiSessionStore : IApiSessionStore
        {
            public Task<ApiSession> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<ApiSession>(null);
            public Task SaveAsync(ApiSession session, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
