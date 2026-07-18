using System;
using System.Collections.Generic;

namespace HonestFlow.Models.Licensing
{
    public sealed class LicenseManifest
    {
        private DateTimeOffset _issuedAtUtc;
        private DateTimeOffset _validUntilUtc;

        public int SchemaVersion { get; set; }
        public long Revision { get; set; }

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

        public List<ClientLicense> Clients { get; set; } = new();
    }
}
