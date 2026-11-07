using System;

namespace HonestFlow.Infrastructure.Api
{
    public static class ApiErrorCodes
    {
        public const string DeviceBoundToAnotherClient = "device_bound_to_another_client";
        public const string ServiceRequired = "service_required";
        public const string DeviceDisabled = "device_disabled";
        public const string ClientDisabled = "client_disabled";

        public static bool IsServiceEntitlementDenied(ApiRequestException exception) =>
            exception is { StatusCode: System.Net.HttpStatusCode.Forbidden } &&
            (string.Equals(exception.ErrorCode, ServiceRequired, StringComparison.Ordinal) ||
             string.Equals(exception.ErrorCode, DeviceDisabled, StringComparison.Ordinal) ||
             string.Equals(exception.ErrorCode, ClientDisabled, StringComparison.Ordinal));
    }
}
