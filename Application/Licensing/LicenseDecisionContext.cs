using System;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseDecisionContext
    {
        public string ClientId { get; set; }
        public string DeviceId { get; set; }
        public Version CurrentHonestFlowVersion { get; set; }
        public LicenseManifest Manifest { get; set; }
        public LicenseManifestSource ManifestSource { get; set; }
        public DateTimeOffset? LastSuccessfulOnlineCheckUtc { get; set; }
    }
}
