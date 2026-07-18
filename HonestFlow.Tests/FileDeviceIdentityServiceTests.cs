using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.DeviceIdentity;
using HonestFlow.Infrastructure.DeviceIdentity;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class FileDeviceIdentityServiceTests
    {
        [Fact]
        public async Task GetOrCreateAsync_CreatesRandomGuidOnFirstRun()
        {
            using var fixture = DeviceIdentityFixture.Create();

            DeviceIdentityResult result = await fixture.CreateService()
                .GetOrCreateAsync(CancellationToken.None);

            Assert.Equal(DeviceIdentityStatus.Created, result.Status);
            Assert.True(Guid.TryParseExact(result.DeviceId, "D", out Guid parsed));
            Assert.NotEqual(Guid.Empty, parsed);
            Assert.True(File.Exists(fixture.StatePath));
        }

        [Fact]
        public async Task GetOrCreateAsync_ReturnsSameDeviceIdAcrossServiceInstances()
        {
            using var fixture = DeviceIdentityFixture.Create();
            DeviceIdentityResult firstRun = await fixture.CreateService()
                .GetOrCreateAsync(CancellationToken.None);

            DeviceIdentityResult secondRun = await fixture.CreateService()
                .GetOrCreateAsync(CancellationToken.None);

            Assert.Equal(DeviceIdentityStatus.Existing, secondRun.Status);
            Assert.Equal(firstRun.DeviceId, secondRun.DeviceId);
        }

        [Fact]
        public async Task GetOrCreateAsync_QuarantinesCorruptionAndCreatesNewDeviceId()
        {
            using var fixture = DeviceIdentityFixture.Create();
            DeviceIdentityResult original = await fixture.CreateService()
                .GetOrCreateAsync(CancellationToken.None);
            await File.WriteAllTextAsync(fixture.StatePath, "corrupted-state");

            DeviceIdentityResult recovered = await fixture.CreateService()
                .GetOrCreateAsync(CancellationToken.None);

            Assert.Equal(DeviceIdentityStatus.RecreatedAfterCorruption, recovered.Status);
            Assert.NotEqual(original.DeviceId, recovered.DeviceId);
            Assert.Single(Directory.GetFiles(fixture.Root, "device-identity.dpapi.corrupt-*"));
        }

        [Fact]
        public void DpapiDeviceIdentityProtector_RoundTripsState()
        {
            var protector = new DpapiDeviceIdentityStateProtector();
            byte[] plaintext = Encoding.UTF8.GetBytes("device-identity-state");

            byte[] protectedData = protector.Protect(plaintext);
            byte[] restored = protector.Unprotect(protectedData);

            Assert.False(plaintext.SequenceEqual(protectedData));
            Assert.Equal(plaintext, restored);
        }

        private sealed class DeviceIdentityFixture : IDisposable
        {
            private DeviceIdentityFixture(string root)
            {
                Root = root;
                StatePath = Path.Combine(root, "device-identity.dpapi");
            }

            public string Root { get; }
            public string StatePath { get; }

            public static DeviceIdentityFixture Create()
            {
                return new DeviceIdentityFixture(
                    Path.Combine(Path.GetTempPath(), "HonestFlow.DeviceIdentity.Tests", Guid.NewGuid().ToString("N")));
            }

            public FileDeviceIdentityService CreateService()
            {
                return new FileDeviceIdentityService(StatePath, new PassThroughProtector());
            }

            public void Dispose()
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, true);
            }
        }

        private sealed class PassThroughProtector : IDeviceIdentityStateProtector
        {
            public byte[] Protect(byte[] plaintext) => (byte[])plaintext.Clone();
            public byte[] Unprotect(byte[] protectedData) => (byte[])protectedData.Clone();
        }
    }
}
