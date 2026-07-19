using System;
using System.Collections.Generic;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseDecisionResult
    {
        public LicenseDecisionResult(
            LicenseDecision decision,
            IReadOnlyCollection<LicenseFeature> features,
            string message,
            string technicalCode,
            DateTimeOffset? offlineGraceEndsAtUtc,
            Version minimumRequiredVersion,
            string pointAddress = null)
        {
            Decision = decision;
            Features = features ?? Array.Empty<LicenseFeature>();
            Message = message;
            TechnicalCode = technicalCode;
            OfflineGraceEndsAtUtc = offlineGraceEndsAtUtc;
            MinimumRequiredVersion = minimumRequiredVersion;
            PointAddress = pointAddress;
        }

        public LicenseDecision Decision { get; }
        public IReadOnlyCollection<LicenseFeature> Features { get; }
        public string Message { get; }
        public string TechnicalCode { get; }
        public DateTimeOffset? OfflineGraceEndsAtUtc { get; }
        public Version MinimumRequiredVersion { get; }
        public string PointAddress { get; }
        public bool IsAllowed => Decision == LicenseDecision.Allowed;
    }
}
