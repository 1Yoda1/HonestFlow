using System;
using System.Security.Cryptography;
using System.Text;
using HonestFlow.Application.Licensing;
using HonestFlow.Models.Licensing;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Licensing
{
    public sealed class EcdsaLicenseSignatureVerifier : ILicenseSignatureVerifier
    {
        private readonly LicensePublicKeyRegistry _keyRegistry;

        public EcdsaLicenseSignatureVerifier(LicensePublicKeyRegistry keyRegistry)
        {
            _keyRegistry = keyRegistry ?? throw new ArgumentNullException(nameof(keyRegistry));
        }

        public LicenseSignatureVerificationResult Verify(
            ReadOnlyMemory<byte> manifestBytes,
            ReadOnlyMemory<byte> signatureFileBytes)
        {
            if (signatureFileBytes.IsEmpty)
                return InvalidFile("EmptySignatureFile");

            LicenseSignatureEnvelope envelope;
            try
            {
                string signatureJson = new UTF8Encoding(false, true).GetString(signatureFileBytes.Span);
                envelope = JsonConvert.DeserializeObject<LicenseSignatureEnvelope>(signatureJson);
            }
            catch (JsonException)
            {
                return InvalidFile("MalformedSignatureFile");
            }
            catch (DecoderFallbackException)
            {
                return InvalidFile("InvalidSignatureFileUtf8");
            }

            if (envelope == null ||
                string.IsNullOrWhiteSpace(envelope.KeyId) ||
                string.IsNullOrWhiteSpace(envelope.Algorithm) ||
                string.IsNullOrWhiteSpace(envelope.Signature))
            {
                return InvalidFile("IncompleteSignatureFile");
            }

            if (!string.Equals(
                    envelope.Algorithm,
                    LicenseSignatureEnvelope.EcdsaP256Sha256Algorithm,
                    StringComparison.Ordinal))
            {
                return InvalidFile("UnsupportedSignatureAlgorithm");
            }

            if (!_keyRegistry.TryGetSubjectPublicKeyInfo(envelope.KeyId, out string encodedPublicKey))
            {
                return LicenseSignatureVerificationResult.Failure(
                    LicenseSignatureVerificationStatus.UnknownKeyId,
                    "UnknownKeyId");
            }

            byte[] publicKey;
            try
            {
                publicKey = Convert.FromBase64String(encodedPublicKey);
            }
            catch (FormatException)
            {
                return InvalidPublicKey();
            }

            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(envelope.Signature);
            }
            catch (FormatException)
            {
                return InvalidFile("InvalidSignatureEncoding");
            }

            if (signature.Length == 0)
                return InvalidFile("EmptySignature");

            try
            {
                using ECDsa ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(publicKey, out int bytesRead);
                if (bytesRead != publicKey.Length || ecdsa.KeySize != 256)
                    return InvalidPublicKey();

                bool valid = ecdsa.VerifyData(
                    manifestBytes.Span,
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence);

                return valid
                    ? LicenseSignatureVerificationResult.Valid()
                    : LicenseSignatureVerificationResult.Failure(
                        LicenseSignatureVerificationStatus.InvalidSignature,
                        "SignatureMismatch");
            }
            catch (CryptographicException)
            {
                return InvalidPublicKey();
            }
        }

        private static LicenseSignatureVerificationResult InvalidFile(string errorCode) =>
            LicenseSignatureVerificationResult.Failure(
                LicenseSignatureVerificationStatus.InvalidSignatureFile,
                errorCode);

        private static LicenseSignatureVerificationResult InvalidPublicKey() =>
            LicenseSignatureVerificationResult.Failure(
                LicenseSignatureVerificationStatus.InvalidPublicKey,
                "InvalidPublicKey");
    }
}
