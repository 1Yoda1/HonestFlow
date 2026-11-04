using System;
using System.IO;
using HonestFlow.Infrastructure.Downloads;

namespace HonestFlow.Infrastructure.Updates;

public static class SignedAssetMetadata
{
    public static TrustedAsset Resolve(
        string component, string version, string fileName, string downloadUrl,
        string sha256, long? sizeBytes, string architecture,
        string releaseKeyId, string manifestBase64, string signatureBase64,
        ReleaseManifestVerifier verifier, bool requireSignedManifest)
    {
        bool anySignedField = !string.IsNullOrWhiteSpace(releaseKeyId) ||
            !string.IsNullOrWhiteSpace(manifestBase64) || !string.IsNullOrWhiteSpace(signatureBase64);
        bool completeEnvelope = !string.IsNullOrWhiteSpace(releaseKeyId) &&
            !string.IsNullOrWhiteSpace(manifestBase64) && !string.IsNullOrWhiteSpace(signatureBase64);
        if (anySignedField && !completeEnvelope)
            throw new InvalidDataException("Сервер вернул неполную подпись дистрибутива.");

        if (completeEnvelope)
        {
            VerifiedReleaseManifest signed = verifier.Verify(new SignedReleaseManifest
            {
                KeyId = releaseKeyId,
                ManifestBase64 = manifestBase64,
                SignatureBase64 = signatureBase64
            }, component, architecture);
            if (!string.Equals(signed.Version, version, StringComparison.Ordinal) ||
                !string.Equals(signed.FileName, fileName, StringComparison.Ordinal) ||
                signed.SizeBytes != sizeBytes ||
                !string.Equals(signed.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Подписанный манифест не совпадает с каталогом дистрибутивов.");
            return new TrustedAsset
            {
                FileName = signed.FileName, DownloadUrl = downloadUrl,
                Sha256 = signed.Sha256, SizeBytes = signed.SizeBytes
            };
        }
        if (requireSignedManifest)
            throw new InvalidDataException("Дистрибутив не имеет обязательной подписи издателя.");
        return new TrustedAsset
        {
            FileName = fileName, DownloadUrl = downloadUrl, Sha256 = sha256, SizeBytes = sizeBytes
        };
    }
}
