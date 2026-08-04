using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Licensing;
using HonestFlow.Infrastructure.Updates;
using HonestFlow.LicenseSigning;
using Newtonsoft.Json;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class SelfUpdateManifestVerifierTests
    {
        [Fact]
        public async Task ValidSignedManifestAndAsset_AreAccepted()
        {
            using TestTrust trust = TestTrust.Create();
            byte[] asset = Encoding.UTF8.GetBytes("test executable bytes");
            SelfUpdateManifest manifest = CreateManifest(asset);
            byte[] manifestBytes = Serialize(manifest);
            byte[] signature = trust.Sign(manifestBytes);
            string path = WriteTemporaryFile(asset);

            try
            {
                SelfUpdateManifest verified = trust.Verifier.VerifyManifest(manifestBytes, signature);
                await trust.Verifier.VerifyDownloadedFileAsync(path, verified);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ChangedManifest_IsRejectedBySignature()
        {
            using TestTrust trust = TestTrust.Create();
            byte[] original = Serialize(CreateManifest(new byte[] { 1, 2, 3 }));
            byte[] signature = trust.Sign(original);
            byte[] changed = Serialize(CreateManifest(new byte[] { 3, 2, 1 }));

            Assert.Throws<InvalidDataException>(
                () => trust.Verifier.VerifyManifest(changed, signature));
        }

        [Fact]
        public async Task ChangedAsset_IsRejectedBySha256()
        {
            using TestTrust trust = TestTrust.Create();
            byte[] expected = new byte[] { 1, 2, 3 };
            SelfUpdateManifest manifest = CreateManifest(expected);
            string path = WriteTemporaryFile(new byte[] { 3, 2, 1 });

            try
            {
                await Assert.ThrowsAsync<InvalidDataException>(
                    () => trust.Verifier.VerifyDownloadedFileAsync(path, manifest));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Theory]
        [InlineData("../HonestFlow.exe")]
        [InlineData("subfolder/HonestFlow.exe")]
        [InlineData("Other.exe")]
        public void UnsafeAssetName_IsRejected(string assetName)
        {
            using TestTrust trust = TestTrust.Create();
            SelfUpdateManifest manifest = CreateManifest(new byte[] { 1 });
            manifest.AssetName = assetName;
            byte[] bytes = Serialize(manifest);

            Assert.Throws<InvalidDataException>(
                () => trust.Verifier.VerifyManifest(bytes, trust.Sign(bytes)));
        }

        [Fact]
        public async Task IncorrectAssetSize_IsRejected()
        {
            using TestTrust trust = TestTrust.Create();
            SelfUpdateManifest manifest = CreateManifest(new byte[] { 1, 2 });
            string path = WriteTemporaryFile(new byte[] { 1 });

            try
            {
                await Assert.ThrowsAsync<InvalidDataException>(
                    () => trust.Verifier.VerifyDownloadedFileAsync(path, manifest));
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static SelfUpdateManifest CreateManifest(byte[] asset) => new()
        {
            SchemaVersion = SelfUpdateManifest.CurrentSchemaVersion,
            Version = "3.1.0.0",
            AssetName = SelfUpdateManifest.ExpectedAssetName,
            Size = asset.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(asset))
        };

        private static byte[] Serialize(SelfUpdateManifest manifest) =>
            new UTF8Encoding(false).GetBytes(JsonConvert.SerializeObject(manifest));

        private static string WriteTemporaryFile(byte[] bytes)
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".exe");
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private sealed class TestTrust : IDisposable
        {
            private readonly ECDsa _key;
            private readonly EcdsaLicenseManifestSigner _signer = new();
            private readonly string _privateKeyPem;

            private TestTrust(ECDsa key)
            {
                _key = key;
                _privateKeyPem = PemEncoding.WriteString("PRIVATE KEY", key.ExportPkcs8PrivateKey());
                var registry = new LicensePublicKeyRegistry(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["test-update-key"] = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())
                    });
                Verifier = new SelfUpdateManifestVerifier(
                    new EcdsaLicenseSignatureVerifier(registry));
            }

            public SelfUpdateManifestVerifier Verifier { get; }
            public static TestTrust Create() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));
            public byte[] Sign(byte[] manifestBytes) =>
                _signer.CreateSignatureFile(manifestBytes, "test-update-key", _privateKeyPem);
            public void Dispose() => _key.Dispose();
        }
    }
}
