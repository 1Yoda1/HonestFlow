using System;
using System.Net;

namespace HonestFlow.Infrastructure.Api
{
    public sealed class ApiRequestException : Exception
    {
        public ApiRequestException(HttpStatusCode statusCode, string errorCode = null)
            : base(string.IsNullOrWhiteSpace(errorCode)
                ? $"API returned HTTP {(int)statusCode}."
                : $"API returned HTTP {(int)statusCode} ({errorCode}).")
        {
            StatusCode = statusCode;
            ErrorCode = errorCode;
        }

        public HttpStatusCode StatusCode { get; }
        public string ErrorCode { get; }
    }
}
