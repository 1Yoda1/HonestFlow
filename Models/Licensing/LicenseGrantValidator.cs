using System;
using System.Collections.Generic;

namespace HonestFlow.Models.Licensing
{
    public static class LicenseGrantValidator
    {
        public static IReadOnlyList<LicenseValidationError> Validate(LicenseGrant grant)
        {
            var errors = new List<LicenseValidationError>();
            if (grant == null)
            {
                errors.Add(new LicenseValidationError(string.Empty, "License grant is required."));
                return errors;
            }

            if (grant.SchemaVersion <= 0)
                errors.Add(new LicenseValidationError(nameof(grant.SchemaVersion), "SchemaVersion must be positive."));
            if (grant.Revision < 0)
                errors.Add(new LicenseValidationError(nameof(grant.Revision), "Revision cannot be negative."));
            if (string.IsNullOrWhiteSpace(grant.ClientId))
                errors.Add(new LicenseValidationError(nameof(grant.ClientId), "ClientId is required."));
            if (string.IsNullOrWhiteSpace(grant.DeviceId))
                errors.Add(new LicenseValidationError(nameof(grant.DeviceId), "DeviceId is required."));
            if (grant.ValidUntilUtc < grant.IssuedAtUtc)
                errors.Add(new LicenseValidationError(nameof(grant.ValidUntilUtc), "ValidUntilUtc cannot be earlier than IssuedAtUtc."));
            if (grant.OfflineGraceHours < 0)
                errors.Add(new LicenseValidationError(nameof(grant.OfflineGraceHours), "OfflineGraceHours cannot be negative."));
            if (grant.PointAddress != null && grant.PointAddress.Trim().Length > 500)
                errors.Add(new LicenseValidationError(nameof(grant.PointAddress), "PointAddress must not exceed 500 characters."));

            return errors;
        }
    }
}
