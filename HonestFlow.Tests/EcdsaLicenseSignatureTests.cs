using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Licensing;
using HonestFlow.LicenseSigning;
using HonestFlow.Models.Licensing;
using Newtonsoft.Json;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class EcdsaLicenseSignatureTests
    {
        [Fact]
        public void Verify_AcceptsCorrectSignature()
        {
            using TestKey key = TestKey.Create("key-2026-01");
            byte[] manifest = Encoding.UTF8.GetBytes("{\"SchemaVersion\":1}");
            byte[] signatureFile = key.Signer.CreateSignatureFile(manifest, key.KeyId, key.PrivateKeyPem);

            LicenseSignatureVerificationResult result = key.Verifier.Verify(manifest, signatureFile);

            Assert.Equal(LicenseSignatureVerificationStatus.Valid, result.Status);
        }

        [Fact]
        public void Verify_RejectsChangedJsonBytes()
        {
            using TestKey key = TestKey.Create("key-2026-01");
            byte[] original = Encoding.UTF8.GetBytes("{\"SchemaVersion\":1}");
            byte[] changed = Encoding.UTF8.GetBytes("{ \"SchemaVersion\":1}");
            byte[] signatureFile = key.Signer.CreateSignatureFile(original, key.KeyId, key.PrivateKeyPem);

            LicenseSignatureVerificationResult result = key.Verifier.Verify(changed, signatureFile);

            Assert.Equal(LicenseSignatureVerificationStatus.InvalidSignature, result.Status);
        }

        [Fact]
        public void Verify_RejectsChangedSignature()
        {
            using TestKey key = TestKey.Create("key-2026-01");
            byte[] manifest = Encoding.UTF8.GetBytes("{\"SchemaVersion\":1}");
            byte[] signatureFile = key.Signer.CreateSignatureFile(manifest, key.KeyId, key.PrivateKeyPem);
            var envelope = JsonConvert.DeserializeObject<LicenseSignatureEnvelope>(
                Encoding.UTF8.GetString(signatureFile));
            byte[] signature = Convert.FromBase64String(envelope.Signature);
            signature[signature.Length - 1] ^= 0x01;
            envelope.Signature = Convert.ToBase64String(signature);
            signatureFile = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope));

            LicenseSignatureVerificationResult result = key.Verifier.Verify(manifest, signatureFile);

            Assert.Equal(LicenseSignatureVerificationStatus.InvalidSignature, result.Status);
        }

        [Fact]
        public void Verify_RejectsUnknownKeyId()
        {
            using TestKey key = TestKey.Create("known-key");
            byte[] manifest = Encoding.UTF8.GetBytes("{}");
            byte[] signatureFile = key.Signer.CreateSignatureFile(
                manifest,
                "unknown-key",
                key.PrivateKeyPem);

            LicenseSignatureVerificationResult result = key.Verifier.Verify(manifest, signatureFile);

            Assert.Equal(LicenseSignatureVerificationStatus.UnknownKeyId, result.Status);
        }

        [Fact]
        public void Verify_RejectsEmptySignatureFile()
        {
            using TestKey key = TestKey.Create("key-2026-01");

            LicenseSignatureVerificationResult result = key.Verifier.Verify(
                Encoding.UTF8.GetBytes("{}"),
                Array.Empty<byte>());

            Assert.Equal(LicenseSignatureVerificationStatus.InvalidSignatureFile, result.Status);
        }

        [Fact]
        public void Verify_RejectsDamagedPublicKey()
        {
            var registry = new LicensePublicKeyRegistry(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["damaged-key"] = "not-a-base64-public-key"
                });
            var verifier = new EcdsaLicenseSignatureVerifier(registry);
            var envelope = new LicenseSignatureEnvelope
            {
                KeyId = "damaged-key",
                Algorithm = LicenseSignatureEnvelope.EcdsaP256Sha256Algorithm,
                Signature = Convert.ToBase64String(new byte[] { 1, 2, 3 })
            };

            LicenseSignatureVerificationResult result = verifier.Verify(
                Encoding.UTF8.GetBytes("{}"),
                Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope)));

            Assert.Equal(LicenseSignatureVerificationStatus.InvalidPublicKey, result.Status);
        }

        private sealed class TestKey : IDisposable
        {
            private readonly ECDsa _key;

            private TestKey(ECDsa key, string keyId)
            {
                _key = key;
                KeyId = keyId;
                PrivateKeyPem = ToPkcs8Pem(key.ExportPkcs8PrivateKey());
                Signer = new EcdsaLicenseManifestSigner();
                var registry = new LicensePublicKeyRegistry(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [keyId] = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())
                    });
                Verifier = new EcdsaLicenseSignatureVerifier(registry);
            }

            public string KeyId { get; }
            public string PrivateKeyPem { get; }
            public EcdsaLicenseManifestSigner Signer { get; }
            public EcdsaLicenseSignatureVerifier Verifier { get; }

            public static TestKey Create(string keyId) =>
                new(ECDsa.Create(ECCurve.NamedCurves.nistP256), keyId);

            private static string ToPkcs8Pem(byte[] privateKey)
            {
                return "-----BEGIN PRIVATE KEY-----\n" +
                       Convert.ToBase64String(privateKey, Base64FormattingOptions.InsertLineBreaks) +
                       "\n-----END PRIVATE KEY-----";
            }

            public void Dispose()
            {
                _key.Dispose();
            }
        }
    }
}
