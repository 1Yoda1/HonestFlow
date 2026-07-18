using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.Licensing
{
    public interface IDeviceRegistrationRequestSender
    {
        Task SendAsync(string requestJson, CancellationToken cancellationToken);
    }
}
