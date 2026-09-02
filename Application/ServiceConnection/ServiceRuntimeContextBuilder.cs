using System;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Bootstrap;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.PointStatus;
using HonestFlow.Application.RemoteAccess;
using HonestFlow.Models;

namespace HonestFlow.Application.ServiceConnection;

public sealed class ServiceRuntimeContextBuilder
{
    public async Task<ServiceConnectionResult> BuildAsync(
        ApplicationStartupController controller,
        ApplicationStartupSession session,
        IPData? client,
        LicenseObservationSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(session);

        ServiceConnectionState state = ServiceEntitlementEvaluator.Evaluate(snapshot);
        if (state != ServiceConnectionState.Active)
        {
            return new ServiceConnectionResult(
                state,
                ServiceEntitlementEvaluator.Message(state, snapshot?.Message));
        }

        if (client is null || session.Startup.CurrentApiConfiguration is null)
        {
            return new ServiceConnectionResult(
                ServiceConnectionState.Unavailable,
                ServiceEntitlementEvaluator.Message(ServiceConnectionState.Unavailable));
        }

        var componentVersions = new ComponentVersionStatusService(session.LogService);
        var pointStatus = new PointStatusService(
            session.Startup.UseRemoteConfigMode,
            session.Startup.Ips?.Count ?? session.Startup.RemoteIps?.Count ?? 0,
            session.Startup.Ips ?? session.Startup.RemoteIps,
            new RuDesktopService(session.LogService));
        var pointStatusRefresh = new PointStatusRefreshService(
            pointStatus,
            componentVersions,
            new PointStatusReportBuilder(),
            session.LogService);
        var context = new ServiceRuntimeContext(
            controller,
            session,
            client,
            snapshot!,
            session.Startup.CurrentApiConfiguration,
            pointStatusRefresh,
            componentVersions);

        await controller.SaveLastAuthorizedClientHintAsync(
            client,
            snapshot!,
            cancellationToken);

        return new ServiceConnectionResult(
            ServiceConnectionState.Active,
            ServiceEntitlementEvaluator.Message(ServiceConnectionState.Active),
            context);
    }
}
