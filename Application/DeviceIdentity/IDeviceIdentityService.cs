using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.DeviceIdentity
{
    public interface IDeviceIdentityService
    {
        Task<DeviceIdentityResult> GetOrCreateAsync(CancellationToken cancellationToken);
    }

    public interface IExistingDeviceIdentityService
    {
        Task<DeviceIdentityResult> TryLoadExistingAsync(CancellationToken cancellationToken);
    }
}
