using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Core;
using HonestFlow.Application.DeviceIdentity;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Models;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class ApiAuthServiceTests
    {
        [Fact]
        public async Task Login_LoadsOnlyCurrentClientConfiguration()
        {
            const string json = "{\"client\":{\"clientId\":\"c1\",\"name\":\"Point\",\"architecture\":\"x64\",\"hasLmDatabaseBackup\":true,\"ruDesktopEnabled\":true},\"device\":{\"deviceId\":\"d1\",\"status\":\"Approved\"},\"components\":[{\"component\":\"LmModule\",\"effectiveVersion\":\"4.2\"}]}";
            var session = new FakeSession(json);
            var service = new ApiAuthService(session, new FakeIdentity(), new FakeLog());

            var result = await service.AuthenticateAsync("seller", "password", null, CancellationToken.None);

            Assert.Equal("seller", session.Login);
            Assert.Equal("c1", result.Client.ClientId);
            Assert.Equal("4.2", result.Client.Versions.LmModule);
            Assert.Null(result.Client.Password);
        }

        [Fact]
        public async Task Resume_NetworkFailureUsesDeviceBoundProtectedCache()
        {
            var cache = new MemoryConfigurationCache(new ApiConfigurationResponse
            {
                Client = new ApiClientConfiguration { ClientId = "cached-client", Name = "Cached" },
                Device = new ApiDeviceConfiguration { DeviceId = "d1" }
            });
            var service = new ApiAuthService(new FailingSession(new HttpRequestException()), new FakeIdentity(), new FakeLog(), cache);

            var result = await service.TryResumeAsync(null, CancellationToken.None);

            Assert.Equal("cached-client", result.Client.ClientId);
        }

        [Fact]
        public async Task Resume_UnauthorizedClearsCacheAndDoesNotFallback()
        {
            var cache = new MemoryConfigurationCache(new ApiConfigurationResponse
            {
                Client = new ApiClientConfiguration { ClientId = "cached-client" },
                Device = new ApiDeviceConfiguration { DeviceId = "d1" }
            });
            var service = new ApiAuthService(
                new FailingSession(new ApiRequestException(HttpStatusCode.Unauthorized)),
                new FakeIdentity(), new FakeLog(), cache);

            var result = await service.TryResumeAsync(null, CancellationToken.None);

            Assert.Null(result.Client);
            Assert.True(cache.Cleared);
        }

        [Fact]
        public async Task Login_PendingDeviceReturnsRegistrationDecisionWithoutConfigurationRequest()
        {
            var session = new PendingDeviceSession();
            var service = new ApiAuthService(session, new FakeIdentity(), new FakeLog(),
                new MemoryConfigurationCache(null));

            var result = await service.AuthenticateAsync("seller", "password", null, CancellationToken.None);

            Assert.Null(result.Client);
            Assert.Equal(HonestFlow.Application.Licensing.LicenseDecision.DeviceNotRegistered,
                result.LicenseSnapshot.Decision);
            Assert.Equal("DEVICE_REGISTRATION_PENDING", result.LicenseSnapshot.TechnicalCode);
            Assert.Equal("api/device/registration/current", session.RequestedPath);
        }

        private sealed class FakeSession : IApiSessionService
        {
            private readonly string _json;
            public FakeSession(string json) { _json = json; }
            public string Login { get; private set; }
            public Task<ApiTokenResponse> LoginAsync(string login, string password, string deviceId, string deviceName, CancellationToken cancellationToken)
            { Login = login; return Task.FromResult(new ApiTokenResponse()); }
            public Task<HttpResponseMessage> SendAuthorizedAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_json) });
        }

        private sealed class FakeIdentity : IDeviceIdentityService
        {
            public Task<DeviceIdentityResult> GetOrCreateAsync(CancellationToken cancellationToken) =>
                Task.FromResult(DeviceIdentityResult.Available(DeviceIdentityStatus.Existing, "d1"));
        }

        private sealed class FailingSession : IApiSessionService
        {
            private readonly Exception _exception;
            public FailingSession(Exception exception) { _exception = exception; }
            public Task<ApiTokenResponse> LoginAsync(string login, string password, string deviceId, string deviceName, CancellationToken cancellationToken) => throw _exception;
            public Task<HttpResponseMessage> SendAuthorizedAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(_exception);
        }

        private sealed class PendingDeviceSession : IApiSessionService
        {
            public string RequestedPath { get; private set; }
            public Task<ApiTokenResponse> LoginAsync(string login, string password, string deviceId, string deviceName, CancellationToken cancellationToken) =>
                Task.FromResult(new ApiTokenResponse { DeviceRegistrationRequired = true });
            public Task<HttpResponseMessage> SendAuthorizedAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestedPath = request.RequestUri.ToString();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"deviceId\":\"d1\",\"status\":\"Pending\",\"requestedAtUtc\":\"2026-08-10T00:00:00Z\"}")
                });
            }
        }

        private sealed class MemoryConfigurationCache : IApiConfigurationCache
        {
            private ApiConfigurationResponse _configuration;
            public MemoryConfigurationCache(ApiConfigurationResponse configuration) { _configuration = configuration; }
            public bool Cleared { get; private set; }
            public Task<ApiConfigurationResponse> LoadAsync(string deviceId, CancellationToken cancellationToken) =>
                Task.FromResult(string.Equals(_configuration?.Device?.DeviceId, deviceId, StringComparison.Ordinal) ? _configuration : null);
            public Task SaveAsync(ApiConfigurationResponse configuration, CancellationToken cancellationToken) { _configuration = configuration; return Task.CompletedTask; }
            public Task ClearAsync(CancellationToken cancellationToken) { Cleared = true; _configuration = null; return Task.CompletedTask; }
        }

        private sealed class FakeLog : ILogService
        {
            public void LogDebug(string message) { }
            public void LogUser(string message, bool isError = false) { }
            public string GetUserLog() => string.Empty;
        }
    }
}
