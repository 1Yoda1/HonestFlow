using System;
using HonestFlow.Application.Licensing;

namespace HonestFlow.Infrastructure.Licensing
{
    public sealed class LicenseRuntimeConfiguration
    {
        public LicenseEnforcementMode EnforcementMode { get; set; } = LicenseEnforcementMode.Enforced;
        public Uri ManifestUrl { get; set; }
        public Uri SignatureUrl { get; set; }
        public string KeyId { get; set; }
        public string PublicKeySubjectPublicKeyInfoBase64 { get; set; }
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
        public int MaxManifestBytes { get; set; } = 1024 * 1024;

        public static LicenseRuntimeConfiguration FromEnvironment()
        {
            // Security-sensitive production settings must not be controlled by the
            // launching user's environment. An elevated process inherits those
            // variables, which previously allowed enforcement and trust to be replaced.
            return new LicenseRuntimeConfiguration();
        }
    }
}
