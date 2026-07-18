using System;
using System.Collections.Generic;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseObservationSnapshot
    {
        public DateTimeOffset ObservedAtUtc { get; set; }
        public string ClientId { get; set; }
        public string DeviceId { get; set; }
        public DateTimeOffset? LastSuccessfulOnlineCheckUtc { get; set; }
        public LicenseEnforcementMode EnforcementMode { get; set; }
        public LicenseManifestSource? ManifestSource { get; set; }
        public LicenseManifestReadStatus RemoteStatus { get; set; }
        public LicenseCacheStatus? CacheStatus { get; set; }
        public LicenseDecision Decision { get; set; }
        public string TechnicalCode { get; set; }
        public string Message { get; set; }
        public DateTimeOffset? OfflineGraceEndsAtUtc { get; set; }
        public Version MinimumRequiredVersion { get; set; }
        public IReadOnlyCollection<LicenseFeature> Features { get; set; } = Array.Empty<LicenseFeature>();
    }
}
