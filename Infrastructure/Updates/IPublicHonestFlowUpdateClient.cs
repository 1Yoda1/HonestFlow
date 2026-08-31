using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Infrastructure.Updates;

public interface IPublicHonestFlowUpdateClient
{
    Task<SelfUpdateInfo?> GetCurrentAsync(CancellationToken cancellationToken);
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}
