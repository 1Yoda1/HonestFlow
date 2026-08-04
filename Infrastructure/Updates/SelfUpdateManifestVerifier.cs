using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Updates
{
    public sealed class SelfUpdateManifestVerifier
    {
        public const long MaximumUpdateBytes = 250L * 1024 * 1024;
        private readonly ILicenseSignatureVerifier _signatureVerifier;

        public SelfUpdateManifestVerifier(ILicenseSignatureVerifier signatureVerifier)
        {
            _signatureVerifier = signatureVerifier ?? throw new ArgumentNullException(nameof(signatureVerifier));
        }

        public SelfUpdateManifest VerifyManifest(
            ReadOnlyMemory<byte> manifestBytes,
            ReadOnlyMemory<byte> signatureBytes)
        {
            LicenseSignatureVerificationResult signature = _signatureVerifier.Verify(
                manifestBytes,
                signatureBytes);
            if (!signature.IsValid)
                throw new InvalidDataException($"UpdateManifestSignatureInvalid:{signature.ErrorCode}");

            SelfUpdateManifest manifest;
            try
            {
                string json = new UTF8Encoding(false, true).GetString(manifestBytes.Span);
                manifest = JsonConvert.DeserializeObject<SelfUpdateManifest>(json);
            }
            catch (Exception ex) when (ex is JsonException || ex is DecoderFallbackException)
            {
                throw new InvalidDataException("UpdateManifestMalformed", ex);
            }

            ValidateManifest(manifest);
            return manifest;
        }

        public async Task VerifyDownloadedFileAsync(string path, SelfUpdateManifest manifest)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidDataException("UpdateAssetMissing");

            var file = new FileInfo(path);
            if (file.Length != manifest.Size)
                throw new InvalidDataException("UpdateAssetSizeMismatch");

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] hash = await SHA256.HashDataAsync(stream);
            string actualHash = Convert.ToHexString(hash);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(actualHash),
                    Encoding.ASCII.GetBytes(manifest.Sha256)))
            {
                throw new InvalidDataException("UpdateAssetSha256Mismatch");
            }
        }

        private static void ValidateManifest(SelfUpdateManifest manifest)
        {
            if (manifest == null)
                throw new InvalidDataException("UpdateManifestEmpty");
            if (manifest.SchemaVersion != SelfUpdateManifest.CurrentSchemaVersion)
                throw new InvalidDataException("UpdateManifestSchemaUnsupported");
            if (!Version.TryParse(manifest.Version, out _))
                throw new InvalidDataException("UpdateManifestVersionInvalid");
            if (string.IsNullOrWhiteSpace(manifest.AssetName) ||
                !string.Equals(manifest.AssetName, Path.GetFileName(manifest.AssetName), StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.AssetName,
                    SelfUpdateManifest.ExpectedAssetName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("UpdateManifestAssetNameInvalid");
            }
            if (manifest.Size <= 0 || manifest.Size > MaximumUpdateBytes)
                throw new InvalidDataException("UpdateManifestSizeInvalid");

            manifest.Sha256 = manifest.Sha256?.Trim().ToUpperInvariant();
            if (manifest.Sha256?.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("UpdateManifestSha256Invalid");
        }
    }
}
