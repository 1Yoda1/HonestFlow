using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.DeviceIdentity
{
    public interface IDeviceIdentityService
    {
        Task<DeviceIdentityResult> GetOrCreateAsync(CancellationToken cancellationToken);
    }
}
