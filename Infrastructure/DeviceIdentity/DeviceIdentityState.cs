using System;

namespace HonestFlow.Infrastructure.DeviceIdentity
{
    internal sealed class DeviceIdentityState
    {
        public int SchemaVersion { get; set; } = 1;
        public string DeviceId { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
    }
}
