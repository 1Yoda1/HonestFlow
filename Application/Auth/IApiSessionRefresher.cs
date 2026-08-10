using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.Auth
{
    public interface IApiSessionRefresher
    {
        Task<bool> RefreshSessionAsync(CancellationToken cancellationToken);
    }
}
