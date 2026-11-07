using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Api;

public static class ApiResponse
{
    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        string failureMessage = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.IsSuccessStatusCode)
            return;

        string errorCode = null;
        try
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            errorCode = JsonConvert.DeserializeObject<ApiErrorBody>(body)?.Code;
        }
        catch (JsonException)
        {
            // Older endpoints and object storage may return non-JSON errors.
        }

        throw new ApiRequestException(response.StatusCode, errorCode, failureMessage);
    }

    private sealed class ApiErrorBody
    {
        public string Code { get; set; }
    }
}
