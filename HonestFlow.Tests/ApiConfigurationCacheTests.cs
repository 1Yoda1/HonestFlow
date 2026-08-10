using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Api;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class ApiConfigurationCacheTests
    {
        [Fact]
        public async Task ProtectedCurrentClientConfiguration_RoundTripsForSameDeviceOnly()
        {
            string directory = Path.Combine(Path.GetTempPath(), "HonestFlowTests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "configuration-current.dpapi");
            try
            {
                var cache = new FileApiConfigurationCache(path, new ReversingProtector());
                var configuration = new ApiConfigurationResponse
                {
                    Client = new ApiClientConfiguration { ClientId = "client-secret", Name = "Point" },
                    Device = new ApiDeviceConfiguration { DeviceId = "device-1" }
                };
                await cache.SaveAsync(configuration, CancellationToken.None);

                string persisted = await File.ReadAllTextAsync(path);
                Assert.DoesNotContain("client-secret", persisted);
                Assert.Equal("client-secret", (await cache.LoadAsync("device-1", CancellationToken.None)).Client.ClientId);
                Assert.Null(await cache.LoadAsync("another-device", CancellationToken.None));
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private sealed class ReversingProtector : IApiSessionProtector
        {
            public byte[] Protect(byte[] plaintext) => Transform(plaintext);
            public byte[] Unprotect(byte[] protectedData) => Transform(protectedData);
            private static byte[] Transform(byte[] bytes)
            {
                byte[] copy = (byte[])bytes.Clone(); Array.Reverse(copy); return copy;
            }
        }
    }
}
