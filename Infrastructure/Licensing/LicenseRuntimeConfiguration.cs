using System;
using HonestFlow.Application.Licensing;

namespace HonestFlow.Infrastructure.Licensing
{
    public sealed class LicenseRuntimeConfiguration
    {
        public LicenseEnforcementMode EnforcementMode { get; set; } = LicenseEnforcementMode.ObserveOnly;
        public Uri ManifestUrl { get; set; }
        public Uri SignatureUrl { get; set; }
        public string KeyId { get; set; }
        public string PublicKeySubjectPublicKeyInfoBase64 { get; set; }
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
        public int MaxManifestBytes { get; set; } = 1024 * 1024;

        public static LicenseRuntimeConfiguration FromEnvironment()
        {
            var configuration = new LicenseRuntimeConfiguration();
            string modeText = Environment.GetEnvironmentVariable("HONESTFLOW_LICENSE_ENFORCEMENT_MODE");
            if (Enum.TryParse(modeText, true, out LicenseEnforcementMode mode))
                configuration.EnforcementMode = mode;

            string manifestUrl = Environment.GetEnvironmentVariable("HONESTFLOW_LICENSE_MANIFEST_URL");
            if (Uri.TryCreate(manifestUrl, UriKind.Absolute, out Uri parsedManifestUrl))
                configuration.ManifestUrl = parsedManifestUrl;

            string signatureUrl = Environment.GetEnvironmentVariable("HONESTFLOW_LICENSE_SIGNATURE_URL");
            if (Uri.TryCreate(signatureUrl, UriKind.Absolute, out Uri parsedSignatureUrl))
                configuration.SignatureUrl = parsedSignatureUrl;
            else if (configuration.ManifestUrl != null)
                configuration.SignatureUrl = new Uri(configuration.ManifestUrl.AbsoluteUri + ".sig");

            configuration.KeyId = Environment.GetEnvironmentVariable("HONESTFLOW_LICENSE_KEY_ID");
            configuration.PublicKeySubjectPublicKeyInfoBase64 = Environment.GetEnvironmentVariable("HONESTFLOW_LICENSE_PUBLIC_KEY");
            return configuration;
        }
    }
}
