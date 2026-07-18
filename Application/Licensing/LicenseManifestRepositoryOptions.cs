using System;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseManifestRepositoryOptions
    {
        public Uri ManifestUrl { get; set; }
        public Uri SignatureUrl { get; set; }
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
        public int MaxResponseBytes { get; set; } = 1024 * 1024;
        public int MaxSignatureResponseBytes { get; set; } = 16 * 1024;
        public int SupportedSchemaVersion { get; set; } = 1;

        public void Validate()
        {
            if (ManifestUrl == null || !ManifestUrl.IsAbsoluteUri)
                throw new ArgumentException("An absolute licenses.json URL is required.", nameof(ManifestUrl));

            if (ManifestUrl.Scheme != Uri.UriSchemeHttp && ManifestUrl.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("The licenses.json URL must use HTTP or HTTPS.", nameof(ManifestUrl));

            if (SignatureUrl == null || !SignatureUrl.IsAbsoluteUri)
                throw new ArgumentException("An absolute licenses.json.sig URL is required.", nameof(SignatureUrl));

            if (SignatureUrl.Scheme != Uri.UriSchemeHttp && SignatureUrl.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("The licenses.json.sig URL must use HTTP or HTTPS.", nameof(SignatureUrl));

            if (RequestTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(RequestTimeout));

            if (MaxResponseBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxResponseBytes));

            if (MaxSignatureResponseBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxSignatureResponseBytes));

            if (SupportedSchemaVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(SupportedSchemaVersion));
        }
    }
}
