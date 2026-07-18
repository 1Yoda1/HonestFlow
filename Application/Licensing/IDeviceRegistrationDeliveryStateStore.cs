using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.Licensing
{
    public interface IDeviceRegistrationDeliveryStateStore
    {
        Task<bool> WasSentAsync(string clientId, string deviceId, CancellationToken cancellationToken);
        Task MarkSentAsync(string clientId, string deviceId, CancellationToken cancellationToken);
    }
}
