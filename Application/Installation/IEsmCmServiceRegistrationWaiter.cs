using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.Installation
{
    public interface IEsmCmServiceRegistrationWaiter
    {
        Task<bool> WaitForServiceAsync(string registrationId, CancellationToken cancellationToken);
    }
}
