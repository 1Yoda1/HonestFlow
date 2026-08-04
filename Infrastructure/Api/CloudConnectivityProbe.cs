using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.PointStatus;

namespace HonestFlow.Infrastructure.Api
{
    public sealed class CloudConnectivityProbe : ICloudConnectivityProbe
    {
        private static readonly HttpClient SharedHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
        {
            using var response = await SharedHttpClient.GetAsync(
                "https://cloud-api.yandex.net/v1/disk/", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode ||
                   (int)response.StatusCode == 401 ||
                   (int)response.StatusCode == 404;
        }
    }
}
