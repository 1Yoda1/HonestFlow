using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Infrastructure.Api
{
    /// <summary>
    /// Provides the authenticated client's most recent configuration/current document.
    /// It keeps installation and update metadata tied to the same server response as
    /// the current client and component versions.
    /// </summary>
    public interface IApiConfigurationProvider
    {
        ApiConfigurationResponse CurrentConfiguration { get; }
    }

    /// <summary>
    /// Loads integration secrets only after the server has checked the current
    /// Service entitlement for this client and device.
    /// </summary>
    public interface IApiServiceConfigurationProvider
    {
        Task<ApiServiceConfigurationResponse> LoadServiceConfigurationAsync(CancellationToken cancellationToken);
    }
}
