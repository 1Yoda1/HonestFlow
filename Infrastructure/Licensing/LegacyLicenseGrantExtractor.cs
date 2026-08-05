using System;
using System.Collections.Generic;
using System.Linq;
using HonestFlow.Application.Licensing;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Infrastructure.Licensing
{
    public static class LegacyLicenseGrantExtractor
    {
        public static LicenseGrant Extract(
            LicenseManifest manifest,
            LicenseGrantRequest request)
        {
            if (manifest == null || request == null ||
                LicenseManifestValidator.Validate(manifest).Count > 0)
                return null;

            OperatorDevice operatorDevice = (manifest.OperatorDevices ?? new List<OperatorDevice>())
                .FirstOrDefault(candidate => candidate != null &&
                    string.Equals(candidate.DeviceId, request.DeviceId, StringComparison.OrdinalIgnoreCase));
            if (operatorDevice != null)
            {
                return new LicenseGrant
                {
                    SchemaVersion = manifest.SchemaVersion,
                    Revision = manifest.Revision,
                    IssuedAtUtc = manifest.IssuedAtUtc,
                    ValidUntilUtc = manifest.ValidUntilUtc,
                    ClientId = request.ClientId,
                    DeviceId = request.DeviceId,
                    ClientEnabled = true,
                    DeviceEnabled = operatorDevice.Enabled,
                    OperatorDevice = true,
                    MinHonestFlowVersion = "0.0.0",
                    OfflineGraceHours = 24,
                    Features = new List<LicenseFeature>()
                };
            }

            ClientLicense client = (manifest.Clients ?? new List<ClientLicense>())
                .FirstOrDefault(candidate => candidate != null &&
                    string.Equals(candidate.ClientId, request.ClientId, StringComparison.Ordinal));
            LicensedDevice device = (client?.Devices ?? new List<LicensedDevice>())
                .FirstOrDefault(candidate => candidate != null &&
                    string.Equals(candidate.DeviceId, request.DeviceId, StringComparison.Ordinal));
            if (client == null || device == null)
                return null;

            return new LicenseGrant
            {
                SchemaVersion = manifest.SchemaVersion,
                Revision = manifest.Revision,
                IssuedAtUtc = manifest.IssuedAtUtc,
                ValidUntilUtc = manifest.ValidUntilUtc,
                ClientId = client.ClientId,
                DeviceId = device.DeviceId,
                ClientEnabled = client.Enabled,
                DeviceEnabled = device.Enabled,
                MinHonestFlowVersion = client.MinHonestFlowVersion,
                OfflineGraceHours = client.OfflineGraceHours,
                Features = client.Features?.Distinct().ToList() ?? new List<LicenseFeature>(),
                PointAddress = device.Address
            };
        }
    }
}
