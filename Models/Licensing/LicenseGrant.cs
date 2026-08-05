using System;
using System.Collections.Generic;

namespace HonestFlow.Models.Licensing
{
    /// <summary>
    /// A minimal signed entitlement for exactly one client/device pair.
    /// It intentionally contains no directory of other clients or devices.
    /// </summary>
    public sealed class LicenseGrant
    {
        private DateTimeOffset _issuedAtUtc;
        private DateTimeOffset _validUntilUtc;

        public int SchemaVersion { get; set; }
        public long Revision { get; set; }
        public string ClientId { get; set; }
        public string DeviceId { get; set; }
        public bool ClientEnabled { get; set; }
        public bool DeviceEnabled { get; set; }
        public bool OperatorDevice { get; set; }
        public string MinHonestFlowVersion { get; set; }
        public int OfflineGraceHours { get; set; }
        public List<LicenseFeature> Features { get; set; } = new();
        public string PointAddress { get; set; }

        public DateTimeOffset IssuedAtUtc
        {
            get => _issuedAtUtc;
            set => _issuedAtUtc = value.ToUniversalTime();
        }

        public DateTimeOffset ValidUntilUtc
        {
            get => _validUntilUtc;
            set => _validUntilUtc = value.ToUniversalTime();
        }
    }
}
