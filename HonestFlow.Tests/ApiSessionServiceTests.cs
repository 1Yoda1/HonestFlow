using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Api;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class ApiSessionServiceTests
    {
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
