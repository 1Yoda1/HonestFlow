using System;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.Licensing
{
    public interface IDeviceRegistrationStatusProvider
    {
        Task<DeviceRegistrationStatus> GetCurrentAsync(CancellationToken cancellationToken);
    }

    public sealed class DeviceRegistrationStatus
    {
        public string DeviceId { get; set; }
        public string Status { get; set; }
        public DateTimeOffset RequestedAtUtc { get; set; }
        public DateTimeOffset? ResolvedAtUtc { get; set; }
        public string Comment { get; set; }
    }
}
