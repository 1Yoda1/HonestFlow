using System;
using HonestFlow.Infrastructure;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseOperationGuard : ILicenseOperationGuard
    {
        private readonly ILicenseAccessPolicy _policy;

        public LicenseOperationGuard(ILicenseAccessPolicy policy)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public void Demand(LicenseOperation operation)
        {
            LicenseAccessResult access = _policy.Check(operation);
            if (access.IsAllowed)
                return;

            Logger.Warning(
                $"Event=LicenseServiceBoundaryDenied Operation={operation} " +
                $"TechnicalCode={access.TechnicalCode}",
                nameof(LicenseOperationGuard));
            throw new LicenseOperationDeniedException(operation, access);
        }
    }
}
