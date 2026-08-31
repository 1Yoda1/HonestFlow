using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Downloads;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class VerifiedAssetDownloaderTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "HonestFlow.Tests", Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task ValidAsset_IsCachedOnlyAfterSizeAndSha256Verification()
        {
            byte[] bytes = "trusted installer"u8.ToArray();
            var sender = new BytesSender(bytes);
            var asset = Asset(bytes);

            string path = await new VerifiedAssetDownloader().GetVerifiedAsync(
                asset, _folder, sender.SendAsync, null, CancellationToken.None);

            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            Assert.Equal(1, sender.Calls);
            Assert.False(File.Exists(path + ".partial"));
        }

        [Fact]
        public async Task WrongSha256_IsRejectedAndNeverStoredOrReturned()
        {
            byte[] bytes = "tampered installer"u8.ToArray();
            var sender = new BytesSender(bytes);
            var asset = Asset("expected installer"u8.ToArray());

            await Assert.ThrowsAsync<InvalidDataException>(() => new VerifiedAssetDownloader().GetVerifiedAsync(
                asset, _folder, sender.SendAsync, null, CancellationToken.None));

            Assert.False(File.Exists(Path.Combine(_folder, asset.FileName)));
            Assert.False(File.Exists(Path.Combine(_folder, asset.FileName + ".partial")));
        }

        [Fact]
        public async Task CorruptedCachedFile_IsDiscardedAndRedownloadedBeforeUse()
        {
            byte[] trusted = "correct installer bytes"u8.ToArray();
            var asset = Asset(trusted);
            Directory.CreateDirectory(_folder);
            await File.WriteAllBytesAsync(Path.Combine(_folder, asset.FileName), "corrupted"u8.ToArray());
            var sender = new BytesSender(trusted);

            string path = await new VerifiedAssetDownloader().GetVerifiedAsync(
                asset, _folder, sender.SendAsync, null, CancellationToken.None);

            Assert.Equal(trusted, await File.ReadAllBytesAsync(path));
            Assert.Equal(1, sender.Calls);
        }

        [Fact]
        public async Task StalePartialFile_IsNeverUsed()
        {
            byte[] trusted = "correct installer bytes"u8.ToArray();
            var asset = Asset(trusted);
            Directory.CreateDirectory(_folder);
            await File.WriteAllBytesAsync(Path.Combine(_folder, asset.FileName + ".partial"), "partial"u8.ToArray());
            var sender = new BytesSender(trusted);

            string path = await new VerifiedAssetDownloader().GetVerifiedAsync(
                asset, _folder, sender.SendAsync, null, CancellationToken.None);

            Assert.Equal(trusted, await File.ReadAllBytesAsync(path));
            Assert.False(File.Exists(path + ".partial"));
        }

        [Fact]
        public async Task MissingSha256_IsRejectedWithoutHttpRequest()
        {
            byte[] bytes = "installer"u8.ToArray();
            var sender = new BytesSender(bytes);
            var asset = new TrustedAsset
            {
                FileName = "asset.exe",
                DownloadUrl = "https://api.honestflow.ru/api/assets/ESM/1/download",
                SizeBytes = bytes.LongLength
            };

            await Assert.ThrowsAsync<InvalidDataException>(() => new VerifiedAssetDownloader().GetVerifiedAsync(
                asset, _folder, sender.SendAsync, null, CancellationToken.None));

            Assert.Equal(0, sender.Calls);
        }

        public void Dispose()
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, recursive: true);
        }

        private static TrustedAsset Asset(byte[] bytes) => new()
        {
            FileName = "asset.exe",
            DownloadUrl = "https://api.honestflow.ru/api/assets/ESM/1/download",
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            SizeBytes = bytes.LongLength
        };

        private sealed class BytesSender
        {
            private readonly byte[] _bytes;
            public BytesSender(byte[] bytes) => _bytes = bytes;
            public int Calls { get; private set; }
            public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_bytes)
                });
            }
        }
    }
}
