using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Updates;

/// <summary>
/// Validates the publisher signature before interpreting any release metadata.
/// The caller must use only the returned fields for version and file verification.
/// </summary>
public sealed class ReleaseManifestVerifier
{
    private const int MaxManifestBytes = 16 * 1024;
    private readonly IReadOnlyDictionary<string, byte[]> _trustedPublicKeys;

    public ReleaseManifestVerifier(IReadOnlyDictionary<string, byte[]> trustedPublicKeys) =>
        _trustedPublicKeys = trustedPublicKeys ?? throw new ArgumentNullException(nameof(trustedPublicKeys));

    public VerifiedReleaseManifest Verify(SignedReleaseManifest signed, string expectedComponent,
        string expectedArchitecture)
    {
        if (signed is null || string.IsNullOrWhiteSpace(signed.KeyId) ||
            !_trustedPublicKeys.TryGetValue(signed.KeyId, out byte[] publicKey) ||
            publicKey is null || publicKey.Length == 0)
            throw new InvalidDataException("Неизвестный ключ подписи обновления.");
        if (string.IsNullOrWhiteSpace(signed.ManifestBase64) || signed.ManifestBase64.Length > 24_000 ||
            string.IsNullOrWhiteSpace(signed.SignatureBase64) || signed.SignatureBase64.Length > 1_024)
            throw new InvalidDataException("Некорректный подписанный манифест обновления.");

        byte[] manifestBytes;
        byte[] signature;
        try
        {
            manifestBytes = Convert.FromBase64String(signed.ManifestBase64);
            signature = Convert.FromBase64String(signed.SignatureBase64);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("Некорректная кодировка подписи обновления.", ex);
        }
        if (manifestBytes.Length is 0 or > MaxManifestBytes || signature.Length is 0 or > 256)
            throw new InvalidDataException("Некорректный размер подписанного манифеста обновления.");

        try
        {
            using ECDsa verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(publicKey, out int bytesRead);
            if (bytesRead != publicKey.Length || verifier.KeySize != 256 ||
                verifier.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7" ||
                !verifier.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence))
                throw new InvalidDataException("Подпись манифеста обновления не прошла проверку.");
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException("Не удалось проверить ключ или подпись обновления.", ex);
        }

        ReleaseManifestData data;
        try
        {
            data = JsonConvert.DeserializeObject<ReleaseManifestData>(
                new System.Text.UTF8Encoding(false, true).GetString(manifestBytes));
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException)
        {
            throw new InvalidDataException("Подписанный манифест обновления повреждён.", ex);
        }
        if (data is null || !string.Equals(data.Component, expectedComponent, StringComparison.OrdinalIgnoreCase) ||
            !IsValidReleaseVersion(data.Component, data.Version) ||
            string.IsNullOrWhiteSpace(data.FileName) ||
            data.FileName != Path.GetFileName(data.FileName) ||
            (data.Architecture != expectedArchitecture && data.Architecture != "any") ||
            data.SizeBytes <= 0 || data.Sha256?.Length != 64 || !IsSha256(data.Sha256))
            throw new InvalidDataException("Подписанный манифест обновления содержит недопустимые поля.");

        return new VerifiedReleaseManifest
        {
            Component = data.Component,
            Version = data.Version,
            FileName = data.FileName,
            Architecture = data.Architecture,
            SizeBytes = data.SizeBytes,
            Sha256 = data.Sha256
        };
    }

    private static bool IsSha256(string value)
    {
        try { return Convert.FromHexString(value).Length == 32; }
        catch (FormatException) { return false; }
    }

    private static bool IsValidReleaseVersion(string component, string version)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 100 ||
            !char.IsAsciiLetterOrDigit(version[0]) ||
            !char.IsAsciiLetterOrDigit(version[^1]) ||
            version.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')))
            return false;

        return !string.Equals(component, "HonestFlow", StringComparison.OrdinalIgnoreCase) ||
            Version.TryParse(version, out _);
    }

    private sealed class ReleaseManifestData
    {
        [JsonProperty("component")] public string Component { get; set; }
        [JsonProperty("version")] public string Version { get; set; }
        [JsonProperty("fileName")] public string FileName { get; set; }
        [JsonProperty("architecture")] public string Architecture { get; set; }
        [JsonProperty("sizeBytes")] public long SizeBytes { get; set; }
        [JsonProperty("sha256")] public string Sha256 { get; set; }
    }
}
