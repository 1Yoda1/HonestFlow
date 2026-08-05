using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Licensing;
using HonestFlow.LicenseSigning;
using HonestFlow.Models.Licensing;
using Newtonsoft.Json;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class FileLicenseManifestCacheTests
    {
        [Fact]
        public async Task SaveAndRead_PreservesOnlyMatchingGrant()
        {
            using var fixture = Fixture.Create();
            Assert.True((await fixture.Save(3)).IsSuccess);
            LicenseCacheReadResult read = await fixture.Cache.ReadAsync(fixture.Request, CancellationToken.None);
            Assert.True(read.IsSuccess);
            Assert.Equal(3, read.Grant.Revision);
            Assert.Equal(fixture.Request.DeviceId, read.Grant.DeviceId);
        }

        [Fact]
        public async Task Read_DoesNotExposeAnotherDevicesCache()
        {
            using var fixture = Fixture.Create();
            await fixture.Save(3);
            LicenseCacheReadResult read = await fixture.Cache.ReadAsync(
                new LicenseGrantRequest("client-1", "other-device"),
                CancellationToken.None);
            Assert.Equal(LicenseCacheStatus.NotFound, read.Status);
        }

        [Fact]
        public async Task Read_RejectsTamperedGrant()
        {
            using var fixture = Fixture.Create();
            await fixture.Save(3);
            File.WriteAllText(fixture.ActiveFile("grant.json"), "{}");
            Assert.Equal(LicenseCacheStatus.InvalidCache,
                (await fixture.Cache.ReadAsync(fixture.Request, CancellationToken.None)).Status);
        }

        private sealed class Fixture : IDisposable
        {
            private readonly ECDsa _key;
            private readonly string _privatePem;
            private readonly EcdsaLicenseManifestSigner _signer = new();
            private const string KeyId = "test";

            private Fixture(string root, ECDsa key)
            {
                Root = root;
                _key = key;
                _privatePem = "-----BEGIN PRIVATE KEY-----\n" + Convert.ToBase64String(key.ExportPkcs8PrivateKey(), Base64FormattingOptions.InsertLineBreaks) + "\n-----END PRIVATE KEY-----";
                var verifier = new EcdsaLicenseSignatureVerifier(new LicensePublicKeyRegistry(
                    new Dictionary<string, string> { [KeyId] = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) }));
                Cache = new FileLicenseManifestCache(root, verifier, new PassThroughProtector());
            }

            public string Root { get; }
            public FileLicenseManifestCache Cache { get; }
            public LicenseGrantRequest Request { get; } = new("client-1", "device-1");

            public static Fixture Create() => new(
                Path.Combine(Path.GetTempPath(), "HonestFlow.Tests", Guid.NewGuid().ToString("N")),
                ECDsa.Create(ECCurve.NamedCurves.nistP256));

            public Task<LicenseCacheWriteResult> Save(long revision)
            {
                var grant = new LicenseGrant
                {
                    SchemaVersion = 1, Revision = revision, ClientId = Request.ClientId, DeviceId = Request.DeviceId,
                    ClientEnabled = true, DeviceEnabled = true, MinHonestFlowVersion = "3.0.0", OfflineGraceHours = 24,
                    IssuedAtUtc = DateTimeOffset.UtcNow.AddDays(-1), ValidUntilUtc = DateTimeOffset.UtcNow.AddDays(7)
                };
                byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(grant));
                byte[] signature = _signer.CreateSignatureFile(bytes, KeyId, _privatePem);
                return Cache.SaveAsync(Request, LicenseManifestReadResult.Success(grant, bytes, signature), DateTimeOffset.UtcNow, CancellationToken.None);
            }

            public string ActiveFile(string name)
            {
                string subjectRoot = Path.Combine(Root, Request.GetOpaquePathId());
                return Path.Combine(subjectRoot, File.ReadAllText(Path.Combine(subjectRoot, "current")).Trim(), name);
            }

            public void Dispose()
            {
                _key.Dispose();
                if (Directory.Exists(Root)) Directory.Delete(Root, true);
            }
        }

        private sealed class PassThroughProtector : ILicenseCacheMetadataProtector
        {
            public byte[] Protect(byte[] plaintext) => (byte[])plaintext.Clone();
            public byte[] Unprotect(byte[] protectedData) => (byte[])protectedData.Clone();
        }
    }
}
