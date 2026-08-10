using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Infrastructure.Api
{
    public interface IApiConfigurationCache
    {
        Task<ApiConfigurationResponse> LoadAsync(string deviceId, CancellationToken cancellationToken);
        Task SaveAsync(ApiConfigurationResponse configuration, CancellationToken cancellationToken);
        Task ClearAsync(CancellationToken cancellationToken);
    }
}
