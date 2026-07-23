using System;
using System.Collections.Generic;
using System.Linq;

namespace HonestFlow.Models.Licensing
{
    public static class LicenseManifestValidator
    {
        public static IReadOnlyList<LicenseValidationError> Validate(LicenseManifest manifest)
        {
            var errors = new List<LicenseValidationError>();

            if (manifest == null)
            {
                errors.Add(new LicenseValidationError(string.Empty, "License manifest is required."));
                return errors;
            }

            if (manifest.Revision < 0)
                errors.Add(new LicenseValidationError(nameof(manifest.Revision), "Revision cannot be negative."));

            if (manifest.ValidUntilUtc < manifest.IssuedAtUtc)
            {
                errors.Add(new LicenseValidationError(
                    nameof(manifest.ValidUntilUtc),
                    "ValidUntilUtc cannot be earlier than IssuedAtUtc."));
            }

            ValidateOperatorDevices(manifest.OperatorDevices, errors);

            var clients = manifest.Clients ?? new List<ClientLicense>();
            var duplicateClientIds = clients
                .Where(client => client != null && !string.IsNullOrWhiteSpace(client.ClientId))
                .GroupBy(client => client.ClientId.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (int clientIndex = 0; clientIndex < clients.Count; clientIndex++)
            {
                ClientLicense client = clients[clientIndex];
                string clientPath = $"Clients[{clientIndex}]";

                if (client == null)
                {
                    errors.Add(new LicenseValidationError(clientPath, "Client license is required."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(client.ClientId))
                {
                    errors.Add(new LicenseValidationError($"{clientPath}.ClientId", "ClientId is required."));
                }
                else if (duplicateClientIds.Contains(client.ClientId.Trim()))
                {
                    errors.Add(new LicenseValidationError($"{clientPath}.ClientId", "ClientId must be unique."));
                }

                if (client.OfflineGraceHours < 0)
                {
                    errors.Add(new LicenseValidationError(
                        $"{clientPath}.OfflineGraceHours",
                        "OfflineGraceHours cannot be negative."));
                }

                ValidateDevices(client.Devices, clientPath, errors);
            }

            return errors;
        }

        private static void ValidateOperatorDevices(
            List<OperatorDevice> devices,
            List<LicenseValidationError> errors)
        {
            devices ??= new List<OperatorDevice>();
            var duplicateDeviceIds = devices
                .Where(device => device != null && !string.IsNullOrWhiteSpace(device.DeviceId))
                .GroupBy(device => device.DeviceId.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (int deviceIndex = 0; deviceIndex < devices.Count; deviceIndex++)
            {
                OperatorDevice device = devices[deviceIndex];
                string devicePath = $"OperatorDevices[{deviceIndex}]";
                if (device == null)
                {
                    errors.Add(new LicenseValidationError(devicePath, "Operator device is required."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(device.DeviceId))
                {
                    errors.Add(new LicenseValidationError(
                        $"{devicePath}.DeviceId",
                        "Operator DeviceId is required."));
                }
                else if (duplicateDeviceIds.Contains(device.DeviceId.Trim()))
                {
                    errors.Add(new LicenseValidationError(
                        $"{devicePath}.DeviceId",
                        "Operator DeviceId must be unique."));
                }
            }
        }

        private static void ValidateDevices(
            List<LicensedDevice> devices,
            string clientPath,
            List<LicenseValidationError> errors)
        {
            devices ??= new List<LicensedDevice>();
            var duplicateDeviceIds = devices
                .Where(device => device != null && !string.IsNullOrWhiteSpace(device.DeviceId))
                .GroupBy(device => device.DeviceId.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (int deviceIndex = 0; deviceIndex < devices.Count; deviceIndex++)
            {
                LicensedDevice device = devices[deviceIndex];
                if (device == null)
                    continue;

                if (device.Address != null && device.Address.Trim().Length > 500)
                {
                    errors.Add(new LicenseValidationError(
                        $"{clientPath}.Devices[{deviceIndex}].Address",
                        "Point address must not exceed 500 characters."));
                }

                if (string.IsNullOrWhiteSpace(device.DeviceId))
                    continue;

                if (duplicateDeviceIds.Contains(device.DeviceId.Trim()))
                {
                    errors.Add(new LicenseValidationError(
                        $"{clientPath}.Devices[{deviceIndex}].DeviceId",
                        "DeviceId must be unique within the client."));
                }
            }
        }
    }
}
