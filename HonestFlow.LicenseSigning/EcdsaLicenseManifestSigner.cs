using System;
using System.Security.Cryptography;
using System.Text;
using HonestFlow.Models.Licensing;
using Newtonsoft.Json;

namespace HonestFlow.LicenseSigning
{
    public sealed class EcdsaLicenseManifestSigner
    {
        public byte[] CreateSignatureFile(
            ReadOnlyMemory<byte> manifestBytes,
            string keyId,
            string privateKeyPkcs8Pem)
        {
            if (manifestBytes.IsEmpty)
                throw new ArgumentException("Manifest bytes are required.", nameof(manifestBytes));

            if (string.IsNullOrWhiteSpace(keyId))
                throw new ArgumentException("KeyId is required.", nameof(keyId));

            if (string.IsNullOrWhiteSpace(privateKeyPkcs8Pem))
                throw new ArgumentException("A PKCS#8 PEM private key is required.", nameof(privateKeyPkcs8Pem));

            using ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(privateKeyPkcs8Pem);
            if (ecdsa.KeySize != 256)
                throw new CryptographicException("The private key must use the ECDSA P-256 curve.");

            byte[] signature = ecdsa.SignData(
                manifestBytes.ToArray(),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);

            var envelope = new LicenseSignatureEnvelope
            {
                KeyId = keyId.Trim(),
                Algorithm = LicenseSignatureEnvelope.EcdsaP256Sha256Algorithm,
                Signature = Convert.ToBase64String(signature)
            };

            string json = JsonConvert.SerializeObject(envelope, Formatting.Indented);
            return new UTF8Encoding(false).GetBytes(json);
        }
    }
}
