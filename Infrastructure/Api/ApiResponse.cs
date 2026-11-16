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
            ApiErrorBody error = JsonConvert.DeserializeObject<ApiErrorBody>(body);
            errorCode = error?.Code ?? error?.Error;
        }
        catch (JsonException)
        {
            // Older endpoints and object storage may return non-JSON errors.
        }

        throw new ApiRequestException(response.StatusCode, errorCode,
            BuildFailureMessage(response, errorCode, failureMessage));
    }

    private static string BuildFailureMessage(
        HttpResponseMessage response,
        string errorCode,
        string failureMessage)
    {
        string prefix = string.IsNullOrWhiteSpace(failureMessage)
            ? "Сервер отклонил запрос."
            : failureMessage.Trim();
        string reason = response.StatusCode switch
        {
            System.Net.HttpStatusCode.TooManyRequests =>
                " Слишком много запросов за короткое время. Повторите попытку позже.",
            System.Net.HttpStatusCode.ServiceUnavailable when
                string.Equals(errorCode, "diagnostic_storage_unavailable", StringComparison.Ordinal) =>
                " Хранилище диагностики временно недоступно. Повторите попытку позже.",
            System.Net.HttpStatusCode.Conflict when
                string.Equals(errorCode, "remote_access_not_ready", StringComparison.Ordinal) =>
                " Быстрый доступ HonestDesk больше не активен. Настройте его заново.",
            _ => string.Empty
        };
        string technical = string.IsNullOrWhiteSpace(errorCode)
            ? $"HTTP {(int)response.StatusCode}"
            : $"HTTP {(int)response.StatusCode}, {errorCode}";
        return $"{prefix}{reason} ({technical})";
    }

    private sealed class ApiErrorBody
    {
        public string Code { get; set; }
        public string Error { get; set; }
    }
}
