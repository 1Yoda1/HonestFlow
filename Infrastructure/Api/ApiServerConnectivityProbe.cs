using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.PointStatus;

namespace HonestFlow.Infrastructure.Api;

public sealed class ApiServerConnectivityProbe
{
    private readonly IApiSessionService _session;

    public ApiServerConnectivityProbe(IApiSessionService session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public async Task<HonestFlowCloudStatus> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/configuration/current");
            using HttpResponseMessage response = await _session.SendAuthorizedAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return HonestFlowCloudStatus.Available;

            return (int)response.StatusCode >= 500
                ? HonestFlowCloudStatus.Unavailable
                : HonestFlowCloudStatus.Unknown;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return HonestFlowCloudStatus.Unavailable;
        }
        catch (TaskCanceledException)
        {
            return HonestFlowCloudStatus.Unavailable;
        }
        catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable ||
                                             ex.StatusCode == HttpStatusCode.GatewayTimeout ||
                                             (int)ex.StatusCode >= 500)
        {
            return HonestFlowCloudStatus.Unavailable;
        }
        catch (ApiRequestException)
        {
            return HonestFlowCloudStatus.Unknown;
        }
        catch (Exception)
        {
            return HonestFlowCloudStatus.Unknown;
        }
    }
}
