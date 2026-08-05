using System;
using System.Security.Cryptography;
using System.Text;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseGrantRequest
    {
        public LicenseGrantRequest(string clientId, string deviceId)
        {
            ClientId = string.IsNullOrWhiteSpace(clientId)
                ? throw new ArgumentException("ClientId is required.", nameof(clientId))
                : clientId;
            DeviceId = string.IsNullOrWhiteSpace(deviceId)
                ? throw new ArgumentException("DeviceId is required.", nameof(deviceId))
                : deviceId;
        }

        public string ClientId { get; }
        public string DeviceId { get; }

        public string GetOpaquePathId()
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ClientId + "\n" + DeviceId));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
