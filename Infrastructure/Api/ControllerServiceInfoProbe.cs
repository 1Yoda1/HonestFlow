using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.PointStatus;

namespace HonestFlow.Infrastructure.Api;

public sealed class ControllerServiceInfoProbe : IControllerServiceInfoProbe, IDisposable
{
    private static readonly Uri ServiceInfoUri = new("http://127.0.0.1:5063/api/v1/service-info");
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    private static readonly HttpClient SharedHttpClient = new();
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public ControllerServiceInfoProbe() : this(SharedHttpClient) { }

    public ControllerServiceInfoProbe(HttpClient httpClient, bool ownsClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsClient = ownsClient;
    }

    public async Task<ControllerServiceInfoResult> CheckAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(ServiceInfoUri, timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? ControllerServiceInfoResult.Available((int)response.StatusCode)
                : ControllerServiceInfoResult.Unavailable("http_error", (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return ControllerServiceInfoResult.Unavailable("timeout");
        }
        catch (HttpRequestException)
        {
            return ControllerServiceInfoResult.Unavailable("connection_refused");
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }
}
