using System;

namespace HonestFlow.Application.Licensing
{
    public sealed class DeviceRegistrationRequest
    {
        public int SchemaVersion { get; set; } = 1;
        public string ClientId { get; set; }
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public string HonestFlowVersion { get; set; }
        public DateTimeOffset RequestedAtUtc { get; set; }
    }
}
