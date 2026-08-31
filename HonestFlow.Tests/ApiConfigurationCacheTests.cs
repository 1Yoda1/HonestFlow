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
                    Client = new ApiClientConfiguration
                    {
                        ClientId = "client-secret", Name = "Point", Inn = "007701234567"
                    },
                    Device = new ApiDeviceConfiguration { DeviceId = "device-1" },
                    Components =
                    {
                        new ApiComponentConfiguration
                        {
                            Component = "ESM", EffectiveVersion = "4.2.0", FileName = "esm.exe",
                            DownloadUrl = "https://api.honestflow.ru/api/assets/ESM/4.2.0/download",
                            Sha256 = new string('a', 64), SizeBytes = 1234, Architecture = "x64"
                        }
                    }
                };
                await cache.SaveAsync(configuration, CancellationToken.None);

                string persisted = await File.ReadAllTextAsync(path);
                Assert.DoesNotContain("client-secret", persisted);
                ApiConfigurationResponse restored = await cache.LoadAsync("device-1", CancellationToken.None);
                Assert.Equal("client-secret", restored.Client.ClientId);
                Assert.Equal("007701234567", restored.Client.Inn);
                Assert.Equal("esm.exe", restored.Components[0].FileName);
                Assert.Equal(1234, restored.Components[0].SizeBytes);
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
