using System;
using HonestFlow.Application.ServiceConnection;
using HonestFlow.Infrastructure;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.Licensing;

/// <summary>
/// Keeps Service-only mutations behind a validated live Service runtime.
/// UI visibility is not an authorization boundary.
/// </summary>
public sealed class ServiceOperationGuard : ILicenseOperationGuard
{
    private readonly Func<ServiceRuntimeContext?> _runtimeProvider;

    public ServiceOperationGuard(Func<ServiceRuntimeContext?> runtimeProvider)
    {
        _runtimeProvider = runtimeProvider ?? throw new ArgumentNullException(nameof(runtimeProvider));
    }

    public void Demand(LicenseOperation operation)
    {
        ServiceRuntimeContext? runtime = _runtimeProvider();
        if (runtime is not null &&
            ServiceEntitlementEvaluator.Evaluate(runtime.Entitlement) == ServiceConnectionState.Active)
        {
            Logger.Info(
                $"Event=ServiceExecutionAllowed Operation={operation}",
                nameof(ServiceOperationGuard));
            return;
        }

        const string message = "Для выполнения действия подключите активный HonestFlow Service.";
        Logger.Warning(
            $"Event=ServiceExecutionDenied Operation={operation} Reason=ServiceRuntimeInactive",
            nameof(ServiceOperationGuard));
        throw new LicenseOperationDeniedException(
            operation,
            new LicenseAccessResult(false, "SERVICE_RUNTIME_INACTIVE", message));
    }
}
