using System.Net;

namespace HonestFlow.Models
{
    /// <summary>
    /// Универсальный ответ от API с сохранением HTTP-статуса
    /// </summary>
    public class ApiResponse<T>
    {
        public bool IsSuccess { get; set; }
        public HttpStatusCode StatusCode { get; set; }
        public T Data { get; set; }
        public string ErrorMessage { get; set; }
        public string RawResponse { get; set; }
        public double DurationMs { get; set; }

        public static ApiResponse<T> Success(T data, HttpStatusCode statusCode, string rawResponse = null, double durationMs = 0)
        {
            return new ApiResponse<T>
            {
                IsSuccess = true,
                StatusCode = statusCode,
                Data = data,
                RawResponse = rawResponse,
                DurationMs = durationMs,
                ErrorMessage = null
            };
        }

        public static ApiResponse<T> Failure(HttpStatusCode statusCode, string errorMessage, string rawResponse = null, double durationMs = 0)
        {
            return new ApiResponse<T>
            {
                IsSuccess = false,
                StatusCode = statusCode,
                Data = default,
                RawResponse = rawResponse,
                DurationMs = durationMs,
                ErrorMessage = errorMessage
            };
        }

        public override string ToString()
        {
            return $"[{(IsSuccess ? "OK" : "FAIL")}] Status: {StatusCode}, Duration: {DurationMs:F0}ms, Error: {ErrorMessage ?? "none"}";
        }
    }

    /// <summary>
    /// Упрощённый ответ без данных (для операций типа init)
    /// </summary>
    public class ApiSimpleResponse
    {
        public bool IsSuccess { get; set; }
        public HttpStatusCode StatusCode { get; set; }
        public string ErrorMessage { get; set; }
        public string RawResponse { get; set; }
        public double DurationMs { get; set; }

        public static ApiSimpleResponse Success(HttpStatusCode statusCode, string rawResponse = null, double durationMs = 0)
        {
            return new ApiSimpleResponse
            {
                IsSuccess = true,
                StatusCode = statusCode,
                RawResponse = rawResponse,
                DurationMs = durationMs,
                ErrorMessage = null
            };
        }

        public static ApiSimpleResponse Failure(HttpStatusCode statusCode, string errorMessage, string rawResponse = null, double durationMs = 0)
        {
            return new ApiSimpleResponse
            {
                IsSuccess = false,
                StatusCode = statusCode,
                RawResponse = rawResponse,
                DurationMs = durationMs,
                ErrorMessage = errorMessage
            };
        }
    }
}