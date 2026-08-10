using System;
using System.Net;

namespace HonestFlow.Infrastructure.Api
{
    public sealed class ApiRequestException : Exception
    {
        public ApiRequestException(HttpStatusCode statusCode) : base($"API returned HTTP {(int)statusCode}.")
        {
            StatusCode = statusCode;
        }

        public HttpStatusCode StatusCode { get; }
    }
}
