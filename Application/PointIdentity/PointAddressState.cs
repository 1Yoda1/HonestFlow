using System;

namespace HonestFlow.Application.PointIdentity
{
    public sealed class PointAddressState
    {
        public int SchemaVersion { get; set; } = 1;
        public string DeviceId { get; set; }
        public string Address { get; set; }
        public PointAddressSource Source { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
    }
}
