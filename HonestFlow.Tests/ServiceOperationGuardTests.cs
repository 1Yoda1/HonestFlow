using System;
using HonestFlow.Application.Licensing;
using HonestFlow.Models.Licensing;
using Xunit;

namespace HonestFlow.Tests;

public sealed class ServiceOperationGuardTests
{
    [Fact]
    public void Evaluator_RejectsExpiredEntitlementWithoutWaitingForRefresh()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var entitlement = new LicenseObservationSnapshot
        {
            Decision = LicenseDecision.Allowed,
            ClientId = "client",
            DeviceId = "device",
            ValidUntilUtc = now.AddTicks(-1),
            Features = [LicenseFeature.Service]
        };

        Assert.Equal(
            HonestFlow.Application.ServiceConnection.ServiceConnectionState.Expired,
            HonestFlow.Application.ServiceConnection.ServiceEntitlementEvaluator.Evaluate(entitlement, now));
    }

    [Fact]
    public void Demand_WithoutActiveServiceRuntime_DeniesWorkflowExecution()
    {
        var guard = new ServiceOperationGuard(() => null);

        LicenseOperationDeniedException exception = Assert.Throws<LicenseOperationDeniedException>(
            () => guard.Demand(LicenseOperation.InstallComponents));

        Assert.Equal(LicenseOperation.InstallComponents, exception.Operation);
        Assert.Equal("SERVICE_RUNTIME_INACTIVE", exception.TechnicalCode);
    }
}
