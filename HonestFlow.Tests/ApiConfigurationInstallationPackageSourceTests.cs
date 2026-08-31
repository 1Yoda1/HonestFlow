using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Installation.Planning;
using HonestFlow.Infrastructure.Api;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class ApiConfigurationInstallationPackageSourceTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "HonestFlow.Tests", Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task ServerConfiguration_ProvidesTheOnlyNormalInstallerSource()
        {
            byte[] bytes = "server-selected installer"u8.ToArray();
            var session = new BytesSession(bytes);
            var source = new ApiConfigurationInstallationPackageSource(session, new ApiConfigurationResponse
            {
                Components =
                {
                    new ApiComponentConfiguration
                    {
                        Component = "ESM",
                        EffectiveVersion = "4.2.0",
                        FileName = "esm-4.2.0.exe",
                        DownloadUrl = "https://api.honestflow.ru/api/assets/ESM/4.2.0/download",
                        Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                        SizeBytes = bytes.LongLength,
                        Architecture = "x64"
                    }
                }
            }, cacheFolder: _folder);

            string path = await source.ResolveInstallerAsync(
                InstallationComponent.Esm, null, CancellationToken.None);

            Assert.Equal("4.2.0", source.Versions.ESM);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            Assert.Equal("https://api.honestflow.ru/api/assets/ESM/4.2.0/download", session.RequestUri);
        }

        [Fact]
        public async Task MissingServerSha256_RejectsInstallerWithoutRequest()
        {
            var session = new BytesSession("installer"u8.ToArray());
            var source = new ApiConfigurationInstallationPackageSource(session, new ApiConfigurationResponse
            {
                Components =
                {
                    new ApiComponentConfiguration
                    {
                        Component = "ESM",
                        EffectiveVersion = "4.2.0",
                        FileName = "esm.exe",
                        DownloadUrl = "https://api.honestflow.ru/api/assets/ESM/4.2.0/download",
                        SizeBytes = 9
                    }
                }
            }, cacheFolder: _folder);

            await Assert.ThrowsAsync<InvalidDataException>(() => source.ResolveInstallerAsync(
                InstallationComponent.Esm, null, CancellationToken.None));

            Assert.Null(session.RequestUri);
        }

        public void Dispose()
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, recursive: true);
        }

        private sealed class BytesSession : IApiSessionService
        {
            private readonly byte[] _bytes;
            public BytesSession(byte[] bytes) => _bytes = bytes;
            public string RequestUri { get; private set; }
            public Task<ApiTokenResponse> LoginAsync(string login, string password, string deviceId, string deviceName,
                CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<HttpResponseMessage> SendAuthorizedAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestUri = request.RequestUri!.ToString();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_bytes)
                });
            }
        }
    }
}
