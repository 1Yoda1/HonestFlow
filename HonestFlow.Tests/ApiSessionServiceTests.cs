using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Api;
using Newtonsoft.Json;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class ApiSessionServiceTests
    {
        [Fact]
        public async Task Login_PersistEnabled_CanBeRestoredByNewService()
        {
            var store = new MemoryStore(null);
            using (var loginHttp = new HttpClient(new QueueHandler(TokenResponse("access-1", "refresh-1")))
            {
                BaseAddress = new Uri("https://example.test/")
            })
            {
                var service = new ApiSessionService(loginHttp, store);
                service.SetPersistSession(true);

                await service.LoginAsync("", "secret-password", "device-1", "PC", CancellationToken.None);
            }

            var resumedHandler = new QueueHandler(Json(HttpStatusCode.OK, "{}"));
            using var resumedHttp = new HttpClient(resumedHandler) { BaseAddress = new Uri("https://example.test/") };
            var resumedService = new ApiSessionService(resumedHttp, store);

            using HttpResponseMessage response = await resumedService.SendAuthorizedAsync(
                new HttpRequestMessage(HttpMethod.Get, "api/configuration/current"),
                CancellationToken.None);

            Assert.NotNull(store.Session);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Bearer access-1", resumedHandler.Authorizations[0]);
        }

        [Fact]
        public async Task Login_PersistDisabled_UsesSessionOnlyInCurrentProcess()
        {
            var store = new MemoryStore(null);
            var handler = new QueueHandler(
                TokenResponse("access-1", "refresh-1"),
                Json(HttpStatusCode.OK, "{}"));
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
            var service = new ApiSessionService(http, store);
            service.SetPersistSession(false);

            await service.LoginAsync("", "secret-password", "device-1", "PC", CancellationToken.None);
            using HttpResponseMessage response = await service.SendAuthorizedAsync(
                new HttpRequestMessage(HttpMethod.Get, "api/configuration/current"),
                CancellationToken.None);

            Assert.Null(store.Session);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Bearer access-1", handler.Authorizations[1]);
        }

        [Fact]
        public async Task Login_PersistDisabled_RemovesPreviouslyPersistedSession()
        {
            var store = new MemoryStore(new ApiSession
            {
                AccessToken = "old-access",
                RefreshToken = "old-refresh",
                AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
            });
            using (var loginHttp = new HttpClient(new QueueHandler(TokenResponse("new-access", "new-refresh")))
            {
                BaseAddress = new Uri("https://example.test/")
            })
            {
                var service = new ApiSessionService(loginHttp, store);
                service.SetPersistSession(false);
                await service.LoginAsync("", "secret-password", "device-1", "PC", CancellationToken.None);
            }

            using var nextRunHttp = new HttpClient(new QueueHandler()) { BaseAddress = new Uri("https://example.test/") };
            var nextRun = new ApiSessionService(nextRunHttp, store);

            Assert.Null(store.Session);
            Assert.False(await nextRun.RefreshSessionAsync(CancellationToken.None));
        }

        [Fact]
        public async Task Login_PersistDisabled_RefreshRotationStaysInMemory()
        {
            var store = new MemoryStore(null);
            var handler = new QueueHandler(
                TokenResponse("access-1", "refresh-1"),
                TokenResponse("access-2", "refresh-2"),
                Json(HttpStatusCode.OK, "{}"));
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
            var service = new ApiSessionService(http, store);
            service.SetPersistSession(false);

            await service.LoginAsync("", "secret-password", "device-1", "PC", CancellationToken.None);
            Assert.True(await service.RefreshSessionAsync(CancellationToken.None));
            using HttpResponseMessage response = await service.SendAuthorizedAsync(
                new HttpRequestMessage(HttpMethod.Get, "api/license/current"),
                CancellationToken.None);

            Assert.Null(store.Session);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Bearer access-2", handler.Authorizations[2]);
        }

        [Fact]
        public async Task Login_PersistEnabled_DoesNotPersistPassword()
        {
            const string password = "password-that-must-not-be-stored";
            var store = new MemoryStore(null);
            using var http = new HttpClient(new QueueHandler(TokenResponse("access", "refresh")))
            {
                BaseAddress = new Uri("https://example.test/")
            };
            var service = new ApiSessionService(http, store);
            service.SetPersistSession(true);

            await service.LoginAsync("", password, "device-1", "PC", CancellationToken.None);

            Assert.NotNull(store.Session);
            Assert.DoesNotContain(password, JsonConvert.SerializeObject(store.Session));
        }

        [Fact]
        public async Task Login_CrossClientConflictExposesErrorCodeAndDoesNotPersistSession()
        {
            var store = new MemoryStore(null);
            using var http = new HttpClient(new QueueHandler(Json(
                HttpStatusCode.Conflict,
                "{\"status\":409,\"code\":\"device_bound_to_another_client\"}")))
            {
                BaseAddress = new Uri("https://example.test/")
            };
            var service = new ApiSessionService(http, store);

            ApiRequestException error = await Assert.ThrowsAsync<ApiRequestException>(() =>
                service.LoginAsync("", "client-b-password", "device-1", "PC", CancellationToken.None));

            Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
            Assert.Equal(ApiErrorCodes.DeviceBoundToAnotherClient, error.ErrorCode);
            Assert.Null(store.Session);
        }

        [Fact]
        public async Task ExpiredAccessToken_RefreshesAndPersistsRotatedToken()
        {
            var store = new MemoryStore(new ApiSession
            {
                AccessToken = "expired-access", RefreshToken = "old-refresh",
                AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            });
            var handler = new QueueHandler(
                Json(HttpStatusCode.OK, "{\"accessToken\":\"new-access\",\"refreshToken\":\"rotated-refresh\",\"expiresInSeconds\":900,\"clientId\":\"client-1\",\"clientName\":\"Client 1\"}"),
                Json(HttpStatusCode.OK, "{}"));
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
            var service = new ApiSessionService(http, store);

            using HttpResponseMessage response = await service.SendAuthorizedAsync(
                new HttpRequestMessage(HttpMethod.Get, "api/configuration/current"), CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("rotated-refresh", store.Session.RefreshToken);
            Assert.Equal("client-1", store.Session.ClientId);
            Assert.Equal("Client 1", store.Session.ClientName);
            Assert.Equal("Bearer new-access", handler.Authorizations[1]);
        }

        [Fact]
        public async Task Logout_NetworkFailureStillClearsLocalSession()
        {
            var store = new MemoryStore(new ApiSession
            {
                AccessToken = "access", RefreshToken = "refresh",
                AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
            });
            using var http = new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("https://example.test/") };
            var service = new ApiSessionService(http, store);

            await service.LogoutAsync(CancellationToken.None);

            Assert.Null(store.Session);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RestrictedLogin_DoesNotPersistBeforeRegistrationRequest(bool remember)
        {
            var remembered = new MemoryStore(null);
            var continuation = new MemoryStore(null);
            using var http = new HttpClient(new QueueHandler(RestrictedTokenResponse("access", "refresh")))
            {
                BaseAddress = new Uri("https://example.test/")
            };
            var service = new ApiSessionService(http, remembered, continuation);
            service.SetPersistSession(remember);

            await service.LoginAsync("", "secret-password", "device-1", "PC", CancellationToken.None);

            Assert.Null(remembered.Session);
            Assert.Null(continuation.Session);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task PendingRegistration_PersistsDedicatedContinuationAndCanBeRestored(bool remember)
        {
            var remembered = new MemoryStore(null);
            var continuation = new MemoryStore(null);
            using (var http = new HttpClient(new QueueHandler(RestrictedTokenResponse("access", "refresh")))
            {
                BaseAddress = new Uri("https://example.test/")
            })
            {
                var service = new ApiSessionService(http, remembered, continuation);
                service.SetPersistSession(remember);
                await service.LoginAsync("", "secret-password", "device-1", "PC", CancellationToken.None);
                await service.PersistRegistrationContinuationAsync(CancellationToken.None);
            }

            using var resumedHttp = new HttpClient(new QueueHandler()) { BaseAddress = new Uri("https://example.test/") };
            var resumed = new ApiSessionService(resumedHttp, remembered, continuation);
            ApiSession restored = await resumed.RestoreRegistrationContinuationAsync(CancellationToken.None);

            Assert.NotNull(restored);
            Assert.Equal("device-1", restored.ExternalDeviceId);
            Assert.Equal(remember, restored.RememberActiveSession);
            Assert.Null(remembered.Session);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ApprovedRegistration_ConvertsContinuationAccordingToRememberPreference(bool remember)
        {
            var remembered = new MemoryStore(null);
            var continuation = new MemoryStore(new ApiSession
            {
                AccessToken = "restricted-access", RefreshToken = "restricted-refresh",
                AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10),
                ClientId = "client-1", ClientName = "Client 1", ExternalDeviceId = "device-1",
                RememberActiveSession = remember
            });
            var handler = new QueueHandler(
                TokenResponse("active-access", "active-refresh"),
                Json(HttpStatusCode.OK, "{}"));
            using var http = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://example.test/")
            };
            var service = new ApiSessionService(http, remembered, continuation);
            Assert.NotNull(await service.RestoreRegistrationContinuationAsync(CancellationToken.None));
            service.PrepareForRegistrationCompletion();

            Assert.True(await service.RefreshSessionAsync(CancellationToken.None));
            Assert.Null(continuation.Session);
            Assert.Equal(remember, remembered.Session != null);
            using HttpResponseMessage inProcessResponse = await service.SendAuthorizedAsync(
                new HttpRequestMessage(HttpMethod.Get, "api/configuration/current"), CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, inProcessResponse.StatusCode);
            Assert.Equal("Bearer active-access", handler.Authorizations[1]);
            if (remember)
            {
                Assert.Equal("active-refresh", remembered.Session.RefreshToken);
                var resumedHandler = new QueueHandler(Json(HttpStatusCode.OK, "{}"));
                using var resumedHttp = new HttpClient(resumedHandler) { BaseAddress = new Uri("https://example.test/") };
                var resumed = new ApiSessionService(resumedHttp, remembered, continuation);
                using HttpResponseMessage resumedResponse = await resumed.SendAuthorizedAsync(
                    new HttpRequestMessage(HttpMethod.Get, "api/configuration/current"), CancellationToken.None);
                Assert.Equal(HttpStatusCode.OK, resumedResponse.StatusCode);
                Assert.Equal("Bearer active-access", resumedHandler.Authorizations[0]);
            }
        }

        [Fact]
        public async Task InvalidRegistrationContinuation_IsCleared()
        {
            var remembered = new MemoryStore(null);
            var continuation = new MemoryStore(new ApiSession
            {
                AccessToken = "expired", RefreshToken = "revoked",
                AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                ExternalDeviceId = "device-1"
            });
            using var http = new HttpClient(new QueueHandler(Json(HttpStatusCode.Unauthorized, "{}")))
            {
                BaseAddress = new Uri("https://example.test/")
            };
            var service = new ApiSessionService(http, remembered, continuation);
            Assert.NotNull(await service.RestoreRegistrationContinuationAsync(CancellationToken.None));

            Assert.False(await service.RefreshSessionAsync(CancellationToken.None));
            Assert.Null(continuation.Session);
            Assert.Null(remembered.Session);
        }

        [Fact]
        public async Task Logout_ClearsRegistrationContinuationAndCurrentRestrictedSession()
        {
            var remembered = new MemoryStore(null);
            var continuation = new MemoryStore(new ApiSession
            {
                AccessToken = "restricted-access", RefreshToken = "restricted-refresh",
                AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10),
                ExternalDeviceId = "device-1"
            });
            using var http = new HttpClient(new QueueHandler(Json(HttpStatusCode.NoContent, "")))
            {
                BaseAddress = new Uri("https://example.test/")
            };
            var service = new ApiSessionService(http, remembered, continuation);
            Assert.NotNull(await service.RestoreRegistrationContinuationAsync(CancellationToken.None));

            await service.LogoutAsync(CancellationToken.None);

            Assert.Null(continuation.Session);
            Assert.Null(remembered.Session);
            Assert.False(await service.RefreshSessionAsync(CancellationToken.None));
        }

        [Fact]
        public async Task Login_InternalTokenTimeoutIsBoundedWithoutCancellingCallerToken()
        {
            var handler = new DelayingHandler();
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
            var service = new ApiSessionService(
                http,
                new MemoryStore(null),
                tokenRequestTimeout: TimeSpan.FromMilliseconds(40));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.LoginAsync("", "password", "device-1", "PC", CancellationToken.None));

            Assert.True(handler.RequestTokenWasCancelled);
        }

        [Fact]
        public async Task Login_UserCancellationRemainsObservableByCaller()
        {
            var handler = new DelayingHandler();
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
            var service = new ApiSessionService(
                http,
                new MemoryStore(null),
                tokenRequestTimeout: TimeSpan.FromSeconds(5));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.LoginAsync("", "password", "device-1", "PC", cancellation.Token));

            Assert.True(cancellation.IsCancellationRequested);
        }

        [Fact]
        public async Task Refresh_ServerErrorDoesNotClearRememberedSession()
        {
            var persisted = new ApiSession
            {
                AccessToken = "expired-access",
                RefreshToken = "refresh",
                AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                ExternalDeviceId = "device-1"
            };
            var store = new MemoryStore(persisted);
            using var http = new HttpClient(new QueueHandler(Json(HttpStatusCode.ServiceUnavailable, "{}")))
            {
                BaseAddress = new Uri("https://example.test/")
            };
            var service = new ApiSessionService(http, store);

            ApiRequestException error = await Assert.ThrowsAsync<ApiRequestException>(() =>
                service.RefreshSessionAsync(CancellationToken.None));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
            Assert.Same(persisted, store.Session);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        private static HttpResponseMessage TokenResponse(string accessToken, string refreshToken) =>
            Json(HttpStatusCode.OK,
                $"{{\"accessToken\":\"{accessToken}\",\"refreshToken\":\"{refreshToken}\",\"expiresInSeconds\":900,\"clientId\":\"client-1\",\"clientName\":\"Client 1\"}}");

        private static HttpResponseMessage RestrictedTokenResponse(string accessToken, string refreshToken) =>
            Json(HttpStatusCode.OK,
                $"{{\"accessToken\":\"{accessToken}\",\"refreshToken\":\"{refreshToken}\",\"expiresInSeconds\":900,\"deviceRegistrationRequired\":true,\"clientId\":\"client-1\",\"clientName\":\"Client 1\"}}");

        private sealed class MemoryStore : IApiSessionStore
        {
            public MemoryStore(ApiSession session) { Session = session; }
            public ApiSession Session { get; private set; }
            public Task<ApiSession> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Session);
            public Task SaveAsync(ApiSession session, CancellationToken cancellationToken) { Session = session; return Task.CompletedTask; }
            public Task ClearAsync(CancellationToken cancellationToken) { Session = null; return Task.CompletedTask; }
        }

        private sealed class QueueHandler : HttpMessageHandler
        {
            private readonly Queue<HttpResponseMessage> _responses;
            public QueueHandler(params HttpResponseMessage[] responses) { _responses = new Queue<HttpResponseMessage>(responses); }
            public List<string> Authorizations { get; } = new();
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Authorizations.Add(request.Headers.Authorization?.ToString());
                return Task.FromResult(_responses.Dequeue());
            }
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromException<HttpResponseMessage>(new HttpRequestException("offline"));
        }

        private sealed class DelayingHandler : HttpMessageHandler
        {
            public bool RequestTokenWasCancelled { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    throw new InvalidOperationException("The test request must be cancelled.");
                }
                catch (OperationCanceledException)
                {
                    RequestTokenWasCancelled = true;
                    throw;
                }
            }
        }
    }
}
