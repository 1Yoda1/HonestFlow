using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.Installation
{
    public interface IEsmTsPiotRegistrationClient
    {
        Task<TsPiotRegistrationResult> RegisterAsync(CancellationToken cancellationToken);
    }
}
