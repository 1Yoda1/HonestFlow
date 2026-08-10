using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Infrastructure.Api
{
    public interface IApiSessionStore
    {
        Task<ApiSession> LoadAsync(CancellationToken cancellationToken);
        Task SaveAsync(ApiSession session, CancellationToken cancellationToken);
        Task ClearAsync(CancellationToken cancellationToken);
    }
}
