using System;
using Newtonsoft.Json;

namespace HonestFlow.Application.Licensing
{
    public sealed class DeviceRegistrationRequestService
    {
        public string Create(
            string clientId,
            string deviceId,
            string deviceName,
            string honestFlowVersion,
            DateTimeOffset requestedAtUtc)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("ClientId is required.", nameof(clientId));
            if (string.IsNullOrWhiteSpace(deviceId))
                throw new ArgumentException("DeviceId is required.", nameof(deviceId));

            var request = new DeviceRegistrationRequest
            {
                ClientId = clientId.Trim(),
                DeviceId = deviceId.Trim(),
                DeviceName = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName.Trim(),
                HonestFlowVersion = string.IsNullOrWhiteSpace(honestFlowVersion)
                    ? null
                    : honestFlowVersion.Trim(),
                RequestedAtUtc = requestedAtUtc.ToUniversalTime()
            };
            return JsonConvert.SerializeObject(request, Formatting.Indented);
        }
    }
}
