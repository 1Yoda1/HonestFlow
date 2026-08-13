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
            using var key = new TestEcdsaKey("key-2026-01");
            byte[] manifest = Encoding.UTF8.GetBytes("{\"SchemaVersion\":1}");
            byte[] signatureFile = key.CreateSignatureFile(manifest, key.KeyId, string.Empty);

            LicenseSignatureVerificationResult result = key.CreateVerifier().Verify(manifest, signatureFile);

            Assert.Equal(LicenseSignatureVerificationStatus.Valid, result.Status);
        }

        [Fact]
        public void Verify_RejectsChangedJsonBytes()
        {
            using var key = new TestEcdsaKey("key-2026-01");
            byte[] original = Encoding.UTF8.GetBytes("{\"SchemaVersion\":1}");
            byte[] changed = Encoding.UTF8.GetBytes("{ \"SchemaVersion\":1}");
            byte[] signatureFile = key.CreateSignatureFile(original, key.KeyId, string.Empty);

            LicenseSignatureVerificationResult result = key.CreateVerifier().Verify(changed, signatureFile);

            Assert.Equal(LicenseSignatureVerificationStatus.InvalidSignature, result.Status);
        }

        [Fact]
        public void Verify_RejectsChangedSignature()
        {
            using var key = new TestEcdsaKey("key-2026-01");
            byte[] manifest = Encoding.UTF8.GetBytes("{\"SchemaVersion\":1}");
            byte[] signatureFile = key.CreateSignatureFile(manifest, key.KeyId, string.Empty);
            var envelope = JsonConvert.DeserializeObject<LicenseSignatureEnvelope>(
                Encoding.UTF8.GetString(signatureFile));
            byte[] signature = Convert.FromBase64String(envelope.Signature);
            signature[signature.Length - 1] ^= 0x01;
            envelope.Signature = Convert.ToBase64String(signature);
            signatureFile = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope));

            LicenseSignatureVerificationResult result = key.CreateVerifier().Verify(manifest, signatureFile);

            Assert.Equal(LicenseSignatureVerificationStatus.InvalidSignature, result.Status);
        }

        [Fact]
        public void Verify_RejectsUnknownKeyId()
        {
            using var key = new TestEcdsaKey("known-key");
            byte[] manifest = Encoding.UTF8.GetBytes("{}");
            byte[] signatureFile = key.CreateSignatureFile(
                manifest,
                "unknown-key",
                string.Empty);

            LicenseSignatureVerificationResult result = key.CreateVerifier().Verify(manifest, signatureFile);

            Assert.Equal(LicenseSignatureVerificationStatus.UnknownKeyId, result.Status);
        }

        [Fact]
        public void Verify_RejectsEmptySignatureFile()
        {
            using var key = new TestEcdsaKey("key-2026-01");

            LicenseSignatureVerificationResult result = key.CreateVerifier().Verify(
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

    }
}
