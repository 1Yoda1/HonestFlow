using System;
using System.Security.Cryptography;
using HonestFlow.Infrastructure.Licensing;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class EmbeddedLicenseTrustTests
    {
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
