using System;
using System.Net.Http;

namespace HonestFlow.Infrastructure.Api
{
    /// <summary>
    /// Production trust anchor for HonestLicenseServer HTTP traffic.
    /// This value is intentionally not configurable by the workstation user.
    /// </summary>
    public static class HonestLicenseServerEndpoint
    {
        public const string ProductionBaseUrl = "https://api.honestflow.ru/";

        public static HttpClient CreateClient(TimeSpan timeout)
        {
            return new HttpClient
            {
                BaseAddress = new Uri(ProductionBaseUrl, UriKind.Absolute),
                Timeout = timeout
            };
        }
    }
}
