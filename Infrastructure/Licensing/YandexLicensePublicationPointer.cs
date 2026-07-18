using System;

namespace HonestFlow.Infrastructure.Licensing
{
    public sealed class YandexLicensePublicationPointer
    {
        public long Revision { get; set; }
        public string VersionPath { get; set; }
        public string ManifestSha256 { get; set; }
        public string SignatureSha256 { get; set; }
        public DateTimeOffset PublishedAtUtc { get; set; }
    }
}
