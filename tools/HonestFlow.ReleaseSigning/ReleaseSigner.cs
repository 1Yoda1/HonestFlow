using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HonestFlow.ReleaseSigning;

public sealed record EncryptedReleaseKey(byte[] PrivateKey, byte[] PublicKey, string KeyId);

public sealed record SignedReleaseEnvelope(string KeyId, string ManifestBase64, string SignatureBase64);

public static class ReleaseSigner
{
    private static readonly byte[] KeyFileMagic = Encoding.ASCII.GetBytes("HFRK1");
    private const int SaltLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int KdfIterations = 250_000;

    public static EncryptedReleaseKey CreateKey(ReadOnlySpan<char> password)
    {
        if (password.Length < 16) throw new ArgumentException("Password must contain at least 16 characters.");
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] publicKey = key.ExportSubjectPublicKeyInfo();
        byte[] rawPrivateKey = key.ExportECPrivateKey();
        try
        {
            return new EncryptedReleaseKey(
                EncryptPrivateKey(rawPrivateKey, password), publicKey, GetKeyId(publicKey));
        }
        finally { CryptographicOperations.ZeroMemory(rawPrivateKey); }
    }

    public static SignedReleaseEnvelope Sign(
        byte[] encryptedPrivateKey, ReadOnlySpan<char> password, string filePath,
        string component, string version, string architecture)
    {
        if (string.IsNullOrWhiteSpace(component) || component.Length > 100)
            throw new ArgumentException("Invalid component name.");
        if (!IsValidReleaseVersion(component, version))
            throw new ArgumentException("Invalid release version.");
        if (architecture is not ("x86" or "x64" or "arm64" or "any"))
            throw new ArgumentException("Invalid architecture.");
        FileInfo file = new(filePath);
        if (!file.Exists || file.Length <= 0) throw new FileNotFoundException("Release file is missing or empty.", filePath);
        if (component == "HonestFlow" && file.Name != "HonestFlow.exe")
            throw new ArgumentException("HonestFlow release must be named HonestFlow.exe.");

        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] rawPrivateKey = DecryptPrivateKey(encryptedPrivateKey, password);
        try
        {
            key.ImportECPrivateKey(rawPrivateKey, out int bytesRead);
            if (bytesRead != rawPrivateKey.Length)
                throw new CryptographicException("Invalid release private key.");
        }
        finally { CryptographicOperations.ZeroMemory(rawPrivateKey); }
        if (key.KeySize != 256 ||
            key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7")
            throw new CryptographicException("Release key must be ECDSA P-256.");

        byte[] publicKey = key.ExportSubjectPublicKeyInfo();
        using FileStream input = new(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        long sizeBytes = input.Length;
        if (sizeBytes <= 0) throw new InvalidDataException("Release file is empty.");
        string sha256 = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            component,
            version,
            fileName = file.Name,
            architecture,
            sizeBytes,
            sha256
        });
        byte[] signature = key.SignData(
            manifestBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return new SignedReleaseEnvelope(
            GetKeyId(publicKey), Convert.ToBase64String(manifestBytes), Convert.ToBase64String(signature));
    }

    private static string GetKeyId(byte[] publicKey) =>
        "release_" + Convert.ToHexString(SHA256.HashData(publicKey).AsSpan(0, 12)).ToLowerInvariant();

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

    private static byte[] EncryptPrivateKey(byte[] rawPrivateKey, ReadOnlySpan<char> password)
    {
        byte[] result = new byte[KeyFileMagic.Length + SaltLength + NonceLength + TagLength + rawPrivateKey.Length];
        KeyFileMagic.CopyTo(result, 0);
        Span<byte> salt = result.AsSpan(KeyFileMagic.Length, SaltLength);
        Span<byte> nonce = result.AsSpan(KeyFileMagic.Length + SaltLength, NonceLength);
        Span<byte> tag = result.AsSpan(KeyFileMagic.Length + SaltLength + NonceLength, TagLength);
        Span<byte> ciphertext = result.AsSpan(KeyFileMagic.Length + SaltLength + NonceLength + TagLength);
        RandomNumberGenerator.Fill(salt);
        RandomNumberGenerator.Fill(nonce);
        byte[] derivedKey = Rfc2898DeriveBytes.Pbkdf2(password, salt, KdfIterations, HashAlgorithmName.SHA256, 32);
        try
        {
            using AesGcm cipher = new(derivedKey, TagLength);
            cipher.Encrypt(nonce, rawPrivateKey, ciphertext, tag, KeyFileMagic);
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(derivedKey); }
    }

    private static byte[] DecryptPrivateKey(byte[] encryptedPrivateKey, ReadOnlySpan<char> password)
    {
        int headerLength = KeyFileMagic.Length + SaltLength + NonceLength + TagLength;
        if (encryptedPrivateKey is null || encryptedPrivateKey.Length <= headerLength ||
            !encryptedPrivateKey.AsSpan(0, KeyFileMagic.Length).SequenceEqual(KeyFileMagic))
            throw new CryptographicException("Invalid release key file.");
        ReadOnlySpan<byte> salt = encryptedPrivateKey.AsSpan(KeyFileMagic.Length, SaltLength);
        ReadOnlySpan<byte> nonce = encryptedPrivateKey.AsSpan(KeyFileMagic.Length + SaltLength, NonceLength);
        ReadOnlySpan<byte> tag = encryptedPrivateKey.AsSpan(KeyFileMagic.Length + SaltLength + NonceLength, TagLength);
        ReadOnlySpan<byte> ciphertext = encryptedPrivateKey.AsSpan(headerLength);
        byte[] rawPrivateKey = new byte[ciphertext.Length];
        byte[] derivedKey = Rfc2898DeriveBytes.Pbkdf2(password, salt, KdfIterations, HashAlgorithmName.SHA256, 32);
        try
        {
            using AesGcm cipher = new(derivedKey, TagLength);
            cipher.Decrypt(nonce, ciphertext, tag, rawPrivateKey, KeyFileMagic);
            return rawPrivateKey;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(rawPrivateKey);
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(derivedKey); }
    }
}
