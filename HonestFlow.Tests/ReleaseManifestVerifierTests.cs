using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HonestFlow.Infrastructure.Updates;
using Xunit;

namespace HonestFlow.Tests;

public sealed class ReleaseManifestVerifierTests
{
    private const string KeyId = "release-2026";
    private const string ValidJson = "{\"component\":\"HonestFlow\",\"version\":\"3.1.0\",\"fileName\":\"HonestFlow.exe\",\"architecture\":\"x64\",\"sizeBytes\":12345,\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}";

    [Fact]
    public void ValidSignature_ReturnsOnlySignedFields()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ReleaseManifestVerifier verifier = CreateVerifier(key);

        VerifiedReleaseManifest result = verifier.Verify(Sign(key, ValidJson), "HonestFlow", "x64");

        Assert.Equal("HonestFlow", result.Component);
        Assert.Equal("3.1.0", result.Version);
        Assert.Equal("HonestFlow.exe", result.FileName);
        Assert.Equal("x64", result.Architecture);
        Assert.Equal(12345, result.SizeBytes);
        Assert.Equal(new string('a', 64), result.Sha256);
    }

    [Fact]
    public void ChangedHashOrSize_WithOriginalSignature_IsRejected()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ReleaseManifestVerifier verifier = CreateVerifier(key);
        SignedReleaseManifest signed = Sign(key, ValidJson);
        signed.ManifestBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            ValidJson.Replace("12345", "12346", StringComparison.Ordinal)));

        Assert.Throws<InvalidDataException>(() => verifier.Verify(signed, "HonestFlow", "x64"));
    }

    [Fact]
    public void SignatureFromDifferentKey_IsRejected()
    {
        using ECDsa trustedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Throws<InvalidDataException>(() =>
            CreateVerifier(trustedKey).Verify(Sign(otherKey, ValidJson), "HonestFlow", "x64"));
    }

    [Fact]
    public void UnknownKeyId_IsRejected()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SignedReleaseManifest signed = Sign(key, ValidJson);
        signed.KeyId = "unknown";

        Assert.Throws<InvalidDataException>(() => CreateVerifier(key).Verify(signed, "HonestFlow", "x64"));
    }

    [Fact]
    public void MissingSignature_IsRejected()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SignedReleaseManifest signed = Sign(key, ValidJson);
        signed.SignatureBase64 = null;

        Assert.Throws<InvalidDataException>(() => CreateVerifier(key).Verify(signed, "HonestFlow", "x64"));
    }

    [Theory]
    [InlineData("{\"component\":\"HonestDesk\",\"version\":\"3.1.0\",\"fileName\":\"HonestFlow.exe\",\"architecture\":\"x64\",\"sizeBytes\":12345,\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}", "x64")]
    [InlineData(ValidJson, "x86")]
    public void ValidSignatureForWrongApplicationOrArchitecture_IsRejected(string json, string expectedArchitecture)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Throws<InvalidDataException>(() =>
            CreateVerifier(key).Verify(Sign(key, json), "HonestFlow", expectedArchitecture));
    }

    [Fact]
    public void HonestFlowVersionWithVendorSuffix_IsRejected()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string json = ValidJson.Replace("3.1.0", "3.1.0-10", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() =>
            CreateVerifier(key).Verify(Sign(key, json), "HonestFlow", "x64"));
    }

    private static ReleaseManifestVerifier CreateVerifier(ECDsa key) =>
        new(new Dictionary<string, byte[]> { [KeyId] = key.ExportSubjectPublicKeyInfo() });

    private static SignedReleaseManifest Sign(ECDsa key, string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        return new SignedReleaseManifest
        {
            KeyId = KeyId,
            ManifestBase64 = Convert.ToBase64String(bytes),
            SignatureBase64 = Convert.ToBase64String(key.SignData(
                bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
        };
    }
}
