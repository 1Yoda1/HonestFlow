using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Api;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Updates;

public sealed class PublicHonestFlowUpdateClient : IPublicHonestFlowUpdateClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public PublicHonestFlowUpdateClient(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? HonestLicenseServerEndpoint.CreateClient(TimeSpan.FromSeconds(15));
    }

    public async Task<SelfUpdateInfo?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                "api/updates/honestflow/current?architecture=" + GetArchitecture(), cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            return JsonConvert.DeserializeObject<SelfUpdateInfo>(
                await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            Logger.LogException(ex, "Public SelfUpdate endpoint unavailable; continuing local startup", nameof(PublicHonestFlowUpdateClient));
            return null;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.LogException(ex, "Public SelfUpdate endpoint timed out; continuing local startup", nameof(PublicHonestFlowUpdateClient));
            return null;
        }
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _httpClient.SendAsync(request, cancellationToken);

    public void Dispose()
    {
        if (_ownsClient) _httpClient.Dispose();
    }

    private static string GetArchitecture() => Environment.Is64BitOperatingSystem ? "x64" : "x86";
}
