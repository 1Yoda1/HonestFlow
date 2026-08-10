using System;
using System.Security.Cryptography;
using HonestFlow.Infrastructure.Licensing;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class EmbeddedLicenseTrustTests
    {
        [Fact]
        public void ProductionAliasesResolveToSameVerifiedPublicKey()
        {
            var registry = EmbeddedLicenseTrust.CreateKeyRegistry();

            Assert.Equal(
                registry[EmbeddedLicenseTrust.ProductionKeyId],
                registry[EmbeddedLicenseTrust.Primary2026KeyId]);
            Assert.Equal(
                EmbeddedLicenseTrust.ProductionPublicKeySubjectPublicKeyInfoBase64,
                registry[EmbeddedLicenseTrust.Primary2026KeyId]);
        }
        [Fact]
        public void ProductionPublicKey_IsValidEcdsaP256SubjectPublicKeyInfo()
        {
            var keys = EmbeddedLicenseTrust.CreateKeyRegistry();
            byte[] encoded = Convert.FromBase64String(
                keys[EmbeddedLicenseTrust.ProductionKeyId]);

            using ECDsa key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(encoded, out int bytesRead);

            Assert.Equal(encoded.Length, bytesRead);
            Assert.Equal(256, key.KeySize);
        }
    }
}
