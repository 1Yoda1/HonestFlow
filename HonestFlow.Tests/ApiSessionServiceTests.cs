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

        private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        private static HttpResponseMessage TokenResponse(string accessToken, string refreshToken) =>
            Json(HttpStatusCode.OK,
                $"{{\"accessToken\":\"{accessToken}\",\"refreshToken\":\"{refreshToken}\",\"expiresInSeconds\":900,\"clientId\":\"client-1\",\"clientName\":\"Client 1\"}}");

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
    }
}
