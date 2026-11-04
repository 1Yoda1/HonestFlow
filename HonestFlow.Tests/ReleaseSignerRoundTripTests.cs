using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HonestFlow.Infrastructure.Updates;
using HonestFlow.ReleaseSigning;
using Xunit;

namespace HonestFlow.Tests;

public sealed class ReleaseSignerRoundTripTests
{
    [Fact]
    public void EncryptedOfflineKey_SignsFileThatClientCanVerify()
    {
        const string password = "test-only-password-12345";
        byte[] binary = Encoding.UTF8.GetBytes("harmless release test bytes");
        string directory = Path.Combine(Path.GetTempPath(), "HonestFlow.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string filePath = Path.Combine(directory, "HonestFlow.exe");
            File.WriteAllBytes(filePath, binary);
            EncryptedReleaseKey key = ReleaseSigner.CreateKey(password);
            SignedReleaseEnvelope signed = ReleaseSigner.Sign(
                key.PrivateKey, password, filePath, "HonestFlow", "3.1.0", "x64");
            var verifier = new ReleaseManifestVerifier(new Dictionary<string, byte[]>
            {
                [key.KeyId] = key.PublicKey
            });

            VerifiedReleaseManifest manifest = verifier.Verify(new SignedReleaseManifest
            {
                KeyId = signed.KeyId,
                ManifestBase64 = signed.ManifestBase64,
                SignatureBase64 = signed.SignatureBase64
            }, "HonestFlow", "x64");

            Assert.Equal(binary.Length, manifest.SizeBytes);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(binary)).ToLowerInvariant(), manifest.Sha256);
            Assert.Equal("3.1.0", manifest.Version);
            Assert.ThrowsAny<CryptographicException>(() =>
                ReleaseSigner.Sign(key.PrivateKey, "wrong-password-12345", filePath,
                    "HonestFlow", "3.1.0", "x64"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void WindowsCng_RoundTripsEcPrivateKey()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] raw = key.ExportECPrivateKey();
        using ECDsa imported = ECDsa.Create();
        imported.ImportECPrivateKey(raw, out int bytesRead);
        Assert.Equal(raw.Length, bytesRead);
        Assert.Equal(key.ExportSubjectPublicKeyInfo(), imported.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public void ComponentVersionWithVendorSuffix_CanBeSignedAndVerified()
    {
        const string password = "test-only-password-12345";
        string directory = Path.Combine(Path.GetTempPath(), "HonestFlow.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string filePath = Path.Combine(directory, "regime-2.6.0-10.msi");
            File.WriteAllBytes(filePath, "lm release"u8.ToArray());
            EncryptedReleaseKey key = ReleaseSigner.CreateKey(password);
            SignedReleaseEnvelope signed = ReleaseSigner.Sign(
                key.PrivateKey, password, filePath, "LmModule", "2.6.0-10", "any");
            var verifier = new ReleaseManifestVerifier(new Dictionary<string, byte[]>
            {
                [key.KeyId] = key.PublicKey
            });

            VerifiedReleaseManifest manifest = verifier.Verify(new SignedReleaseManifest
            {
                KeyId = signed.KeyId,
                ManifestBase64 = signed.ManifestBase64,
                SignatureBase64 = signed.SignatureBase64
            }, "LmModule", "x64");

            Assert.Equal("2.6.0-10", manifest.Version);
            Assert.Equal("any", manifest.Architecture);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("../2.6.0")]
    [InlineData("2.6.0/10")]
    [InlineData("2.6.0 10")]
    public void UnsafeComponentVersion_IsRejected(string version)
    {
        const string password = "test-only-password-12345";
        string directory = Path.Combine(Path.GetTempPath(), "HonestFlow.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string filePath = Path.Combine(directory, "component.msi");
            File.WriteAllBytes(filePath, "component"u8.ToArray());
            EncryptedReleaseKey key = ReleaseSigner.CreateKey(password);

            Assert.Throws<ArgumentException>(() => ReleaseSigner.Sign(
                key.PrivateKey, password, filePath, "LmModule", version, "any"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
