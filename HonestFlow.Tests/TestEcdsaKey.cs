using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using HonestFlow.Infrastructure.Licensing;
using HonestFlow.LicenseSigning;
using HonestFlow.Models.Licensing;
using Newtonsoft.Json;

namespace HonestFlow.Tests;

internal sealed class TestEcdsaKey : IDisposable, ILicenseSignatureFileCreator
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public TestEcdsaKey(string keyId) => KeyId = keyId;
    public string KeyId { get; }
    public EcdsaLicenseSignatureVerifier CreateVerifier() => new(new LicensePublicKeyRegistry(
        new Dictionary<string, string> { [KeyId] = Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo()) }));

    public byte[] CreateSignatureFile(ReadOnlyMemory<byte> bytes, string keyId, string ignoredPrivateKeyPem)
    {
        byte[] signature = _key.SignData(bytes.Span, HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        return new UTF8Encoding(false).GetBytes(JsonConvert.SerializeObject(new LicenseSignatureEnvelope
        {
            KeyId = keyId,
            Algorithm = LicenseSignatureEnvelope.EcdsaP256Sha256Algorithm,
            Signature = Convert.ToBase64String(signature)
        }));
    }

    public void Dispose() => _key.Dispose();
}
