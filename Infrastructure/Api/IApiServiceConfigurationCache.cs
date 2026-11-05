using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Infrastructure.Api
{
    public interface IApiServiceConfigurationCache
    {
        Task<ApiServiceConfigurationResponse> LoadAsync(
            string clientId,
            string deviceId,
            CancellationToken cancellationToken);

        Task SaveAsync(ApiServiceConfigurationResponse configuration, CancellationToken cancellationToken);
        Task ClearAsync(CancellationToken cancellationToken);
    }
}
