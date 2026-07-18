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
        public async Task SaveAndRead_PreservesVerifiedSnapshotAndMetadata()
        {
            using var fixture = CacheFixture.Create();
            DateTimeOffset checkedAt = new(2026, 7, 18, 10, 0, 0, TimeSpan.FromHours(6));

            LicenseCacheWriteResult write = await fixture.Cache.SaveAsync(
                fixture.CreateOnlineResult(10),
                checkedAt,
                CancellationToken.None);
            LicenseCacheReadResult read = await fixture.Cache.ReadAsync(CancellationToken.None);

            Assert.True(write.IsSuccess);
            Assert.True(read.IsSuccess);
            Assert.Equal(10, read.Manifest.Revision);
            Assert.Equal(checkedAt.ToUniversalTime(), read.LastSuccessfulOnlineCheckUtc);
        }

        [Fact]
        public async Task Read_ReturnsInvalidCacheForDamagedJson()
        {
            using var fixture = CacheFixture.Create();
            await fixture.SaveRevision(10);
            File.WriteAllText(fixture.ActiveFile("licenses.json"), "{ damaged-json");

            LicenseCacheReadResult result = await fixture.Cache.ReadAsync(CancellationToken.None);

            Assert.Equal(LicenseCacheStatus.InvalidCache, result.Status);
        }

        [Fact]
        public async Task Read_ReturnsInvalidCacheForDamagedSignature()
        {
            using var fixture = CacheFixture.Create();
            await fixture.SaveRevision(10);
            File.WriteAllText(fixture.ActiveFile("licenses.json.sig"), "{\"Signature\":\"damaged\"}");

            LicenseCacheReadResult result = await fixture.Cache.ReadAsync(CancellationToken.None);

            Assert.Equal(LicenseCacheStatus.InvalidCache, result.Status);
        }

        [Fact]
        public async Task Read_IgnoresInterruptedUnpublishedSnapshot()
        {
            using var fixture = CacheFixture.Create();
            string interrupted = Path.Combine(fixture.Root, ".tmp-interrupted");
            Directory.CreateDirectory(interrupted);
            File.WriteAllText(Path.Combine(interrupted, "licenses.json"), "partial");

            LicenseCacheReadResult result = await fixture.Cache.ReadAsync(CancellationToken.None);

            Assert.Equal(LicenseCacheStatus.NotFound, result.Status);
        }

        [Fact]
        public async Task Read_ReturnsNotFoundWhenCacheDoesNotExist()
        {
            using var fixture = CacheFixture.Create();

            LicenseCacheReadResult result = await fixture.Cache.ReadAsync(CancellationToken.None);

            Assert.Equal(LicenseCacheStatus.NotFound, result.Status);
        }

        [Fact]
        public async Task Save_DoesNotReplaceNewerRevisionWithOlderRevision()
        {
            using var fixture = CacheFixture.Create();
            await fixture.SaveRevision(10);

            LicenseCacheWriteResult staleWrite = await fixture.Cache.SaveAsync(
                fixture.CreateOnlineResult(9),
                DateTimeOffset.UtcNow,
                CancellationToken.None);
            LicenseCacheReadResult read = await fixture.Cache.ReadAsync(CancellationToken.None);

            Assert.Equal(LicenseCacheStatus.StaleRevision, staleWrite.Status);
            Assert.Equal(10, read.Manifest.Revision);
        }

        [Fact]
        public void DpapiProtector_RoundTripsMetadataOnWindows()
        {
            var protector = new DpapiLicenseCacheMetadataProtector();
            byte[] plaintext = Encoding.UTF8.GetBytes("metadata-without-license-content");

            byte[] protectedData = protector.Protect(plaintext);
            byte[] restored = protector.Unprotect(protectedData);

            Assert.NotEqual(plaintext, protectedData);
            Assert.Equal(plaintext, restored);
        }

        private sealed class CacheFixture : IDisposable
        {
            private readonly ECDsa _key;
            private readonly EcdsaLicenseManifestSigner _signer;
            private readonly string _privateKeyPem;
            private readonly string _keyId;

            private CacheFixture(
                string root,
                ECDsa key,
                string keyId,
                EcdsaLicenseSignatureVerifier verifier)
            {
                Root = root;
                _key = key;
                _keyId = keyId;
                _privateKeyPem = ToPkcs8Pem(key.ExportPkcs8PrivateKey());
                _signer = new EcdsaLicenseManifestSigner();
                Cache = new FileLicenseManifestCache(root, verifier, new PassThroughProtector());
            }

            public string Root { get; }
            public FileLicenseManifestCache Cache { get; }

            public static CacheFixture Create()
            {
                string root = Path.Combine(Path.GetTempPath(), "HonestFlow.Tests", Guid.NewGuid().ToString("N"));
                var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                const string keyId = "cache-test-key";
                var registry = new LicensePublicKeyRegistry(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [keyId] = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())
                    });
                return new CacheFixture(root, key, keyId, new EcdsaLicenseSignatureVerifier(registry));
            }

            public LicenseManifestReadResult CreateOnlineResult(long revision)
            {
                var manifest = new LicenseManifest
                {
                    SchemaVersion = 1,
                    Revision = revision,
                    IssuedAtUtc = new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero),
                    ValidUntilUtc = new DateTimeOffset(2027, 7, 18, 0, 0, 0, TimeSpan.Zero),
                    Clients = new List<ClientLicense>()
                };
                byte[] manifestBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(manifest));
                byte[] signatureFileBytes = _signer.CreateSignatureFile(
                    manifestBytes,
                    _keyId,
                    _privateKeyPem);
                return LicenseManifestReadResult.Success(manifest, manifestBytes, signatureFileBytes);
            }

            public Task<LicenseCacheWriteResult> SaveRevision(long revision)
            {
                return Cache.SaveAsync(
                    CreateOnlineResult(revision),
                    DateTimeOffset.UtcNow,
                    CancellationToken.None);
            }

            public string ActiveFile(string fileName)
            {
                string snapshot = File.ReadAllText(Path.Combine(Root, "current")).Trim();
                return Path.Combine(Root, snapshot, fileName);
            }

            public void Dispose()
            {
                _key.Dispose();
                if (Directory.Exists(Root))
                    Directory.Delete(Root, true);
            }

            private static string ToPkcs8Pem(byte[] privateKey)
            {
                return "-----BEGIN PRIVATE KEY-----\n" +
                       Convert.ToBase64String(privateKey, Base64FormattingOptions.InsertLineBreaks) +
                       "\n-----END PRIVATE KEY-----";
            }
        }

        private sealed class PassThroughProtector : ILicenseCacheMetadataProtector
        {
            public byte[] Protect(byte[] plaintext) => (byte[])plaintext.Clone();
            public byte[] Unprotect(byte[] protectedData) => (byte[])protectedData.Clone();
        }
    }
}
