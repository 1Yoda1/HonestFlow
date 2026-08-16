using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Core;
using HonestFlow.Application.DeviceIdentity;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Infrastructure.Licensing;
using HonestFlow.Models;
using HonestFlow.Models.Licensing;
using Newtonsoft.Json;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class OfflineStartupHardeningTests : IDisposable
    {
        private const string ClientId = "client-1";
        private const string DeviceId = "device-1";
        private const string KeyId = "offline-test-key";
        private readonly DateTimeOffset _nowUtc = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "HonestFlow.Tests",
            "offline-startup-" + Guid.NewGuid().ToString("N"));
        private readonly TestEcdsaKey _key = new(KeyId);
        private readonly ReversingProtector _protector = new();

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RememberOn_NewServiceInstance_TransientFailureUsesPersistedCaches(
            bool serverReturns503)
        {
            await SeedSuccessfulOnlineStateAsync(remember: true);
            HttpMessageHandler offlineHandler = serverReturns503
                ? new QueueHandler(
                    Json(HttpStatusCode.ServiceUnavailable, "{}"),
                    Json(HttpStatusCode.ServiceUnavailable, "{}"))
                : new ThrowingHandler();
            using var offlineHttp = Client(offlineHandler);
            var session = Session(offlineHttp);
            var auth = Auth(session);

            LicenseAuthenticationResult resumed = await auth.TryResumeAsync(null, CancellationToken.None);
            Assert.NotNull(resumed.Client);

            var observer = new LicenseObservationService(
                new ApiLicenseManifestRepository(session, _key.CreateVerifier(), TimeSpan.FromSeconds(1)),
                LicenseCache(),
                new FixedIdentity(),
                new LicenseDecisionService(new LicenseDecisionPolicy(), () => _nowUtc),
                new LicenseObservationSnapshotStore(),
                LicenseEnforcementMode.Enforced,
                () => _nowUtc,
                () => new Version(3, 0, 0));

            LicenseObservationSnapshot snapshot = await observer.ObserveAsync(
                resumed.Client,
                CancellationToken.None);

            Assert.Equal(LicenseDecision.Allowed, snapshot.Decision);
            Assert.Equal(LicenseManifestSource.Cache, snapshot.ManifestSource);
            Assert.Equal(DeviceId, snapshot.DeviceId);
            Assert.Equal(_nowUtc.AddHours(-1), snapshot.LastSuccessfulOnlineCheckUtc);
            Assert.True(File.Exists(SessionPath));
        }

        [Fact]
        public async Task RememberOff_NewServiceInstanceCannotUseExistingConfigurationOrLicenseCaches()
        {
            await SeedSuccessfulOnlineStateAsync(remember: false);
            var handler = new ThrowingHandler();
            using var offlineHttp = Client(handler);
            var session = Session(offlineHttp);

            LicenseAuthenticationResult resumed = await Auth(session)
                .TryResumeAsync(null, CancellationToken.None);

            Assert.Null(resumed.Client);
            Assert.False(File.Exists(SessionPath));
            Assert.Equal(0, handler.RequestCount);
        }

        [Fact]
        public async Task FirstOfflineStartupWithoutConfirmedStateCannotResume()
        {
            var handler = new ThrowingHandler();
            using var offlineHttp = Client(handler);

            LicenseAuthenticationResult resumed = await Auth(Session(offlineHttp))
                .TryResumeAsync(null, CancellationToken.None);

            Assert.Null(resumed.Client);
            Assert.Equal(0, handler.RequestCount);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task RememberOn_AuthoritativeRefreshDenialDoesNotDowngradeToCaches(
            HttpStatusCode statusCode)
        {
            await SeedSuccessfulOnlineStateAsync(remember: true);
            using var deniedHttp = Client(new QueueHandler(Json(statusCode, "{}")));

            LicenseAuthenticationResult resumed = await Auth(Session(deniedHttp))
                .TryResumeAsync(null, CancellationToken.None);

            Assert.Null(resumed.Client);
            Assert.False(File.Exists(SessionPath));
        }

        private async Task SeedSuccessfulOnlineStateAsync(bool remember)
        {
            Directory.CreateDirectory(_root);
            using var onlineHttp = Client(new QueueHandler(
                Json(HttpStatusCode.OK,
                    "{\"accessToken\":\"access\",\"refreshToken\":\"refresh\",\"expiresInSeconds\":900," +
                    "\"clientId\":\"client-1\",\"clientName\":\"Point\",\"licensePolicyEnabled\":true}"),
                Json(HttpStatusCode.OK,
                    "{\"client\":{\"clientId\":\"client-1\",\"name\":\"Point\",\"architecture\":\"x64\"}," +
                    "\"device\":{\"deviceId\":\"device-1\",\"name\":\"PC\",\"status\":\"Active\"},\"components\":[]}")));
            ApiSessionService session = Session(onlineHttp);
            session.SetPersistSession(remember);

            LicenseAuthenticationResult authentication = await Auth(session).AuthenticateAsync(
                string.Empty,
                "password",
                null,
                CancellationToken.None);
            Assert.Equal(ClientId, authentication.Client.ClientId);

            LicenseGrant grant = Grant();
            byte[] grantBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(grant));
            byte[] signatureBytes = _key.CreateSignatureFile(grantBytes, KeyId, string.Empty);
            LicenseCacheWriteResult cacheWrite = await LicenseCache().SaveAsync(
                new LicenseGrantRequest(ClientId, DeviceId),
                LicenseManifestReadResult.Success(grant, grantBytes, signatureBytes),
                _nowUtc.AddHours(-1),
                CancellationToken.None);
            Assert.True(cacheWrite.IsSuccess);
        }

        private ApiAuthService Auth(IApiSessionService session) => new(
            session,
            new FixedIdentity(),
            new FakeLog(),
            new FileApiConfigurationCache(ConfigurationPath, _protector));

        private ApiSessionService Session(HttpClient httpClient) => new(
            httpClient,
            new FileApiSessionStore(SessionPath, _protector));

        private FileLicenseManifestCache LicenseCache() => new(
            LicenseCachePath,
            _key.CreateVerifier(),
            _protector);

        private string SessionPath => Path.Combine(_root, "api-session.dpapi");
        private string ConfigurationPath => Path.Combine(_root, "configuration-current.dpapi");
        private string LicenseCachePath => Path.Combine(_root, "license-cache");

        private LicenseGrant Grant() => new()
        {
            SchemaVersion = 1,
            Revision = 1,
            ClientId = ClientId,
            DeviceId = DeviceId,
            ClientEnabled = true,
            DeviceEnabled = true,
            MinHonestFlowVersion = "3.0.0",
            OfflineGraceHours = 24,
            IssuedAtUtc = _nowUtc.AddDays(-1),
            ValidUntilUtc = _nowUtc.AddDays(7),
            Features = new List<LicenseFeature> { LicenseFeature.ViewAndRepair }
        };

        private static HttpClient Client(HttpMessageHandler handler) => new(handler)
        {
            BaseAddress = new Uri("https://example.test/"),
            Timeout = Timeout.InfiniteTimeSpan
        };

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) => new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        public void Dispose()
        {
            _key.Dispose();
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private sealed class FixedIdentity : IDeviceIdentityService
        {
            public Task<DeviceIdentityResult> GetOrCreateAsync(CancellationToken cancellationToken) =>
                Task.FromResult(DeviceIdentityResult.Available(DeviceIdentityStatus.Existing, DeviceId));
        }

        private sealed class FakeLog : ILogService
        {
            public void LogDebug(string message) { }
            public void LogUser(string message, bool isError = false) { }
            public string GetUserLog() => string.Empty;
        }

        private sealed class ReversingProtector : IApiSessionProtector, ILicenseCacheMetadataProtector
        {
            public byte[] Protect(byte[] plaintext) => Transform(plaintext);
            public byte[] Unprotect(byte[] protectedData) => Transform(protectedData);

            private static byte[] Transform(byte[] value)
            {
                byte[] copy = (byte[])value.Clone();
                Array.Reverse(copy);
                return copy;
            }
        }

        private sealed class QueueHandler : HttpMessageHandler
        {
            private readonly Queue<HttpResponseMessage> _responses;

            public QueueHandler(params HttpResponseMessage[] responses) =>
                _responses = new Queue<HttpResponseMessage>(responses);

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) =>
                Task.FromResult(_responses.Dequeue());
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            public int RequestCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                RequestCount++;
                return Task.FromException<HttpResponseMessage>(new HttpRequestException("offline"));
            }
        }
    }
}
