using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Infrastructure.Api
{
    public interface IApiSessionService
    {
        Task<ApiTokenResponse> LoginAsync(string login, string password, string deviceId, string deviceName, CancellationToken cancellationToken);
        Task<HttpResponseMessage> SendAuthorizedAsync(HttpRequestMessage request, CancellationToken cancellationToken);
        Task LogoutAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
