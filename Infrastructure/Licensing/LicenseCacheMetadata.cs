using System;

namespace HonestFlow.Infrastructure.Licensing
{
    internal sealed class LicenseCacheMetadata
    {
        public int MetadataVersion { get; set; } = 1;
        public DateTimeOffset LastSuccessfulOnlineCheckUtc { get; set; }
        public int SchemaVersion { get; set; }
        public long Revision { get; set; }
        public string ManifestSha256 { get; set; }
        public string SignatureFileSha256 { get; set; }
    }
}
