namespace HonestFlow.Infrastructure.Updates;

// The server transports these values but must not recreate the signed JSON bytes.
public sealed class SignedReleaseManifest
{
    public string KeyId { get; set; }
    public string ManifestBase64 { get; set; }
    public string SignatureBase64 { get; set; }
}

public sealed class VerifiedReleaseManifest
{
    public string Component { get; init; }
    public string Version { get; init; }
    public string FileName { get; init; }
    public string Architecture { get; init; }
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; }
}
