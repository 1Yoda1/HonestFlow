using HonestFlow.Application.Licensing;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Tests;

internal sealed class AllowLicenseOperationGuard : ILicenseOperationGuard
{
    public void Demand(LicenseOperation operation) { }
}

internal sealed class DenyLicenseOperationGuard : ILicenseOperationGuard
{
    public void Demand(LicenseOperation operation) => throw new LicenseOperationDeniedException(
        operation,
        new LicenseAccessResult(false, "TEST_DENIED", "denied"));
}
