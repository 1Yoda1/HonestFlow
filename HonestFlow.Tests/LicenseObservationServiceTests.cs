using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.DeviceIdentity;
using HonestFlow.Application.Licensing;
using HonestFlow.Models;
using HonestFlow.Models.Licensing;
using Newtonsoft.Json;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class LicenseObservationServiceTests
    {
        private static readonly DateTimeOffset NowUtc =
            new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public async Task Observe_RemoteAvailable_SavesCacheAndUsesRemote()
        {
            var fixture = CreateFixture(SuccessfulRemote(CreateManifest()));

            LicenseObservationSnapshot result = await fixture.Observer.ObserveAsync(Client(), CancellationToken.None);

            Assert.Equal(LicenseDecision.Allowed, result.Decision);
            Assert.Equal(LicenseManifestSource.Remote, result.ManifestSource);
            Assert.Equal(1, fixture.Cache.SaveCalls);
        }

        [Fact]
        public async Task Observe_NetworkUnavailableWithCache_UsesCache()
        {
            var cache = new FakeCache
            {
                ReadResult = LicenseCacheReadResult.Success(CreateManifest(), NowUtc.AddHours(-1))
            };
            var fixture = CreateFixture(
                LicenseManifestReadResult.Failure(
                    LicenseManifestReadStatus.NetworkUnavailable,
                    "network"),
                cache);

            LicenseObservationSnapshot result = await fixture.Observer.ObserveAsync(Client(), CancellationToken.None);

            Assert.Equal(LicenseDecision.Allowed, result.Decision);
            Assert.Equal(LicenseManifestSource.Cache, result.ManifestSource);
        }

        [Fact]
        public async Task Observe_NetworkUnavailableWithoutCache_ReturnsInvalidState()
        {
            var fixture = CreateFixture(LicenseManifestReadResult.Failure(
                LicenseManifestReadStatus.NetworkUnavailable,
                "network"));

            LicenseObservationSnapshot result = await fixture.Observer.ObserveAsync(Client(), CancellationToken.None);

            Assert.Equal(LicenseDecision.InvalidLicenseState, result.Decision);
            Assert.Null(result.ManifestSource);
        }

        [Fact]
        public async Task Observe_ClientMissing_ReturnsClientNotFound()
        {
            var fixture = CreateFixture(SuccessfulRemote(CreateManifest()));
            IPData client = Client();
            client.ClientId = "missing-client";

            LicenseObservationSnapshot result = await fixture.Observer.ObserveAsync(client, CancellationToken.None);

            Assert.Equal(LicenseDecision.ClientNotFound, result.Decision);
        }

        [Fact]
        public async Task Observe_DeviceMissing_ReturnsDeviceNotRegistered()
        {
            var fixture = CreateFixture(
                SuccessfulRemote(CreateManifest()),
                deviceId: "missing-device");

            LicenseObservationSnapshot result = await fixture.Observer.ObserveAsync(Client(), CancellationToken.None);

            Assert.Equal(LicenseDecision.DeviceNotRegistered, result.Decision);
        }

        [Fact]
        public async Task Observe_ClientDisabled_ReturnsClientDisabled()
        {
            LicenseManifest manifest = CreateManifest();
            manifest.Clients[0].Enabled = false;
            var fixture = CreateFixture(SuccessfulRemote(manifest));

            LicenseObservationSnapshot result = await fixture.Observer.ObserveAsync(Client(), CancellationToken.None);

            Assert.Equal(LicenseDecision.ClientDisabled, result.Decision);
        }

        [Fact]
        public async Task Observe_OldApplicationVersion_ReturnsVersionTooOld()
        {
            var fixture = CreateFixture(
                SuccessfulRemote(CreateManifest()),
                version: new Version(2, 4, 1, 0));

            LicenseObservationSnapshot result = await fixture.Observer.ObserveAsync(Client(), CancellationToken.None);

            Assert.Equal(LicenseDecision.VersionTooOld, result.Decision);
        }

        [Fact]
        public async Task Observe_DamagedRemoteLicense_DoesNotUseCache()
        {
            var cache = new FakeCache
            {
                ReadResult = LicenseCacheReadResult.Success(CreateManifest(), NowUtc.AddHours(-1))
            };
            var fixture = CreateFixture(
                LicenseManifestReadResult.Failure(
                    LicenseManifestReadStatus.InvalidSignature,
                    "SignatureMismatch"),
                cache);

            LicenseObservationSnapshot result = await fixture.Observer.ObserveAsync(Client(), CancellationToken.None);

            Assert.Equal(LicenseDecision.InvalidLicenseState, result.Decision);
            Assert.Equal(0, cache.ReadCalls);
        }

        [Fact]
        public async Task Observe_OlderSignedRemoteRevision_UsesNewerVerifiedCache()
        {
            LicenseManifest cachedManifest = CreateManifest();
            cachedManifest.Revision = 8;
            var cache = new FakeCache
            {
                SaveResult = LicenseCacheWriteResult.Failure(
                    LicenseCacheStatus.StaleRevision,
                    "RevisionOlderThanCache"),
                ReadResult = LicenseCacheReadResult.Success(cachedManifest, NowUtc.AddHours(-1))
            };
            LicenseManifest remoteManifest = CreateManifest();
            remoteManifest.Revision = 7;
            var fixture = CreateFixture(SuccessfulRemote(remoteManifest), cache);

            LicenseObservationSnapshot result = await fixture.Observer.ObserveAsync(
                Client(),
                CancellationToken.None);

            Assert.Equal(LicenseManifestSource.Cache, result.ManifestSource);
            Assert.Equal(LicenseDecision.Allowed, result.Decision);
            Assert.Equal(1, cache.ReadCalls);
        }

        private static ObservationFixture CreateFixture(
            LicenseManifestReadResult remoteResult,
            FakeCache cache = null,
            string deviceId = "device-1",
            Version version = null)
        {
            cache ??= new FakeCache
            {
                ReadResult = LicenseCacheReadResult.Failure(LicenseCacheStatus.NotFound, "missing")
            };
            var store = new LicenseObservationSnapshotStore();
            var observer = new LicenseObservationService(
                new FakeRemoteRepository(remoteResult),
                cache,
                new FakeDeviceIdentityService(deviceId),
                new LicenseDecisionService(
                    new LicenseDecisionPolicy
                    {
                        AllowDiagnosticsWhenDenied = true,
                        AllowSendLogsWhenDenied = true
                    },
                    () => NowUtc),
                store,
                LicenseEnforcementMode.ObserveOnly,
                () => NowUtc,
                () => version ?? new Version(2, 4, 2, 0));
            return new ObservationFixture(observer, cache);
        }

        private static LicenseManifestReadResult SuccessfulRemote(LicenseManifest manifest)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(manifest));
            return LicenseManifestReadResult.Success(manifest, bytes, new byte[] { 1 });
        }

        private static IPData Client() => new() { ClientId = "client-1", Name = "Client" };

        private static LicenseManifest CreateManifest()
        {
            return new LicenseManifest
            {
                SchemaVersion = 1,
                Revision = 1,
                IssuedAtUtc = NowUtc.AddDays(-1),
                ValidUntilUtc = NowUtc.AddDays(30),
                Clients = new List<ClientLicense>
                {
                    new ClientLicense
                    {
                        ClientId = "client-1",
                        Enabled = true,
                        MinHonestFlowVersion = "2.4.2.0",
                        OfflineGraceHours = 24,
                        Features = new List<LicenseFeature>
                        {
                            LicenseFeature.Diagnostics,
                            LicenseFeature.SendLogs,
                            LicenseFeature.Install
                        },
                        Devices = new List<LicensedDevice>
                        {
                            new LicensedDevice { DeviceId = "device-1", Enabled = true }
                        }
                    }
                }
            };
        }

        private sealed class ObservationFixture
        {
            public ObservationFixture(LicenseObservationService observer, FakeCache cache)
            {
                Observer = observer;
                Cache = cache;
            }

            public LicenseObservationService Observer { get; }
            public FakeCache Cache { get; }
        }

        private sealed class FakeRemoteRepository : ILicenseManifestRepository
        {
            private readonly LicenseManifestReadResult _result;
            public FakeRemoteRepository(LicenseManifestReadResult result) => _result = result;
            public Task<LicenseManifestReadResult> ReadAsync(CancellationToken cancellationToken) =>
                Task.FromResult(_result);
        }

        private sealed class FakeCache : ILicenseManifestCache
        {
            public LicenseCacheReadResult ReadResult { get; set; }
            public LicenseCacheWriteResult SaveResult { get; set; } = LicenseCacheWriteResult.Success();
            public int ReadCalls { get; private set; }
            public int SaveCalls { get; private set; }

            public Task<LicenseCacheWriteResult> SaveAsync(
                LicenseManifestReadResult onlineResult,
                DateTimeOffset successfulOnlineCheckUtc,
                CancellationToken cancellationToken)
            {
                SaveCalls++;
                return Task.FromResult(SaveResult);
            }

            public Task<LicenseCacheReadResult> ReadAsync(CancellationToken cancellationToken)
            {
                ReadCalls++;
                return Task.FromResult(ReadResult);
            }
        }

        private sealed class FakeDeviceIdentityService : IDeviceIdentityService
        {
            private readonly string _deviceId;
            public FakeDeviceIdentityService(string deviceId) => _deviceId = deviceId;
            public Task<DeviceIdentityResult> GetOrCreateAsync(CancellationToken cancellationToken) =>
                Task.FromResult(DeviceIdentityResult.Available(DeviceIdentityStatus.Existing, _deviceId));
        }
    }
}
