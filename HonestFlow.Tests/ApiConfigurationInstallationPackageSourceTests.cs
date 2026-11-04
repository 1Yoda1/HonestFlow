using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Installation.Planning;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Infrastructure.Updates;
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
            string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            byte[] manifest = Encoding.UTF8.GetBytes(
                $"{{\"component\":\"ESM\",\"version\":\"4.2.0\",\"fileName\":\"esm-4.2.0.exe\",\"architecture\":\"x64\",\"sizeBytes\":{bytes.Length},\"sha256\":\"{sha}\"}}");
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var verifier = new ReleaseManifestVerifier(new Dictionary<string, byte[]>
            {
                ["release-test"] = key.ExportSubjectPublicKeyInfo()
            });
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
                        Sha256 = sha,
                        SizeBytes = bytes.LongLength,
                        Architecture = "x64",
                        ReleaseKeyId = "release-test",
                        ManifestBase64 = Convert.ToBase64String(manifest),
                        ReleaseSignatureBase64 = Convert.ToBase64String(key.SignData(manifest,
                            HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                    }
                }
            }, cacheFolder: _folder, releaseVerifier: verifier);

            string path = await source.ResolveInstallerAsync(
                InstallationComponent.Esm, null, CancellationToken.None);

            Assert.Equal("4.2.0", source.Versions.ESM);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            Assert.Equal("https://api.honestflow.ru/api/assets/ESM/4.2.0/download", session.RequestUri);
        }

        [Fact]
        public void MissingServerSha256_RejectsInstallerAtComposition()
        {
            var session = new BytesSession("installer"u8.ToArray());
            Assert.Throws<InvalidDataException>(() => new ApiConfigurationInstallationPackageSource(
                session,
                new ApiConfigurationResponse
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
            }, cacheFolder: _folder));

            Assert.Null(session.RequestUri);
        }

        [Fact]
        public async Task SignedComponent_UsesOnlyPublisherVerifiedMetadata()
        {
            byte[] bytes = "signed component"u8.ToArray();
            string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            byte[] manifest = Encoding.UTF8.GetBytes(
                $"{{\"component\":\"ESM\",\"version\":\"4.3.0\",\"fileName\":\"esm.exe\",\"architecture\":\"x64\",\"sizeBytes\":{bytes.Length},\"sha256\":\"{sha}\"}}");
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var verifier = new ReleaseManifestVerifier(new Dictionary<string, byte[]>
            {
                ["release-test"] = key.ExportSubjectPublicKeyInfo()
            });
            var session = new BytesSession(bytes);
            var source = new ApiConfigurationInstallationPackageSource(session, new ApiConfigurationResponse
            {
                Components =
                {
                    new ApiComponentConfiguration
                    {
                        Component = "ESM", EffectiveVersion = "4.3.0", FileName = "esm.exe",
                        DownloadUrl = "https://api.honestflow.ru/api/assets/ESM/4.3.0/download",
                        Sha256 = sha, SizeBytes = bytes.Length, Architecture = "x64",
                        ReleaseKeyId = "release-test", ManifestBase64 = Convert.ToBase64String(manifest),
                        ReleaseSignatureBase64 = Convert.ToBase64String(key.SignData(manifest,
                            HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                    }
                }
            }, cacheFolder: _folder, releaseVerifier: verifier, requireSignedManifest: true);

            string path = await source.ResolveInstallerAsync(InstallationComponent.Esm, null, CancellationToken.None);

            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }

        [Fact]
        public async Task SignedComponent_UsesServerSelectedArchitectureInsteadOfWindowsArchitecture()
        {
            string selectedArchitecture = Environment.Is64BitOperatingSystem ? "x86" : "x64";
            byte[] bytes = "server-selected architecture"u8.ToArray();
            string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            byte[] manifest = Encoding.UTF8.GetBytes(
                $"{{\"component\":\"ESM\",\"version\":\"4.3.0\",\"fileName\":\"esm.exe\",\"architecture\":\"{selectedArchitecture}\",\"sizeBytes\":{bytes.Length},\"sha256\":\"{sha}\"}}");
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var verifier = new ReleaseManifestVerifier(new Dictionary<string, byte[]>
            {
                ["release-test"] = key.ExportSubjectPublicKeyInfo()
            });
            var source = new ApiConfigurationInstallationPackageSource(new BytesSession(bytes), new ApiConfigurationResponse
            {
                Components =
                {
                    new ApiComponentConfiguration
                    {
                        Component = "ESM", EffectiveVersion = "4.3.0", FileName = "esm.exe",
                        DownloadUrl = "https://api.honestflow.ru/api/assets/ESM/4.3.0/download",
                        Sha256 = sha, SizeBytes = bytes.Length, Architecture = selectedArchitecture,
                        ReleaseKeyId = "release-test", ManifestBase64 = Convert.ToBase64String(manifest),
                        ReleaseSignatureBase64 = Convert.ToBase64String(key.SignData(manifest,
                            HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                    }
                }
            }, cacheFolder: _folder, releaseVerifier: verifier, requireSignedManifest: true);

            string path = await source.ResolveInstallerAsync(InstallationComponent.Esm, null, CancellationToken.None);

            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }

        [Fact]
        public void SignedManifestForDifferentVersion_IsRejectedBeforeDownload()
        {
            byte[] bytes = "signed component"u8.ToArray();
            string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            byte[] manifest = Encoding.UTF8.GetBytes(
                $"{{\"component\":\"ESM\",\"version\":\"4.3.1\",\"fileName\":\"esm.exe\",\"architecture\":\"x64\",\"sizeBytes\":{bytes.Length},\"sha256\":\"{sha}\"}}");
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var verifier = new ReleaseManifestVerifier(new Dictionary<string, byte[]>
            {
                ["release-test"] = key.ExportSubjectPublicKeyInfo()
            });
            var session = new BytesSession(bytes);

            Assert.Throws<InvalidDataException>(() => new ApiConfigurationInstallationPackageSource(
                session,
                new ApiConfigurationResponse
                {
                    Components =
                    {
                        new ApiComponentConfiguration
                        {
                            Component = "ESM", EffectiveVersion = "4.3.0", FileName = "esm.exe",
                            DownloadUrl = "https://api.honestflow.ru/api/assets/ESM/4.3.0/download",
                            Sha256 = sha, SizeBytes = bytes.Length, Architecture = "x64",
                            ReleaseKeyId = "release-test", ManifestBase64 = Convert.ToBase64String(manifest),
                            ReleaseSignatureBase64 = Convert.ToBase64String(key.SignData(manifest,
                                HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                        }
                    }
                }, cacheFolder: _folder, releaseVerifier: verifier, requireSignedManifest: true));

            Assert.Null(session.RequestUri);
        }

        [Fact]
        public void RequiredSignature_RejectsLegacyComponentBeforeDownload()
        {
            byte[] bytes = "unsigned component"u8.ToArray();
            Assert.Throws<InvalidDataException>(() => new ApiConfigurationInstallationPackageSource(
                new BytesSession(bytes), new ApiConfigurationResponse
                {
                    Components =
                    {
                        new ApiComponentConfiguration
                        {
                            Component = "ESM", EffectiveVersion = "4.4.0", FileName = "esm.exe",
                            DownloadUrl = "https://api.honestflow.ru/api/assets/ESM/4.4.0/download",
                            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), SizeBytes = bytes.Length,
                            Architecture = "x64"
                        }
                    }
                }, cacheFolder: _folder, releaseVerifier: ReleaseTrustStore.CreateVerifier(),
                requireSignedManifest: true));
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
