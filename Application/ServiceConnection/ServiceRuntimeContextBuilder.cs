using System;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Bootstrap;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.PointStatus;
using HonestFlow.Application.RemoteAccess;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Api;
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

        if (session.Startup.AuthService is not IApiServiceConfigurationProvider serviceConfigurationProvider)
        {
            return new ServiceConnectionResult(
                ServiceConnectionState.Unavailable,
                "Не удалось получить защищённую конфигурацию HonestFlow Service.");
        }

        ApiServiceConfigurationResponse serviceConfiguration;
        try
        {
            serviceConfiguration = await serviceConfigurationProvider
                .LoadServiceConfigurationAsync(cancellationToken);
        }
        catch (ApiRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                                             ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return new ServiceConnectionResult(
                ServiceConnectionState.NotEntitled,
                ServiceEntitlementEvaluator.Message(ServiceConnectionState.NotEntitled));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.Warning(
                $"Event=ServiceConfigurationLoadFailed ErrorType={ex.GetType().Name}",
                nameof(ServiceRuntimeContextBuilder));
            return new ServiceConnectionResult(
                ServiceConnectionState.Unavailable,
                "Не удалось получить защищённую конфигурацию HonestFlow Service.");
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
            serviceConfiguration,
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
