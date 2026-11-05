using System;
using HonestFlow.Application.Bootstrap;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.PointStatus;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Models;

namespace HonestFlow.Application.ServiceConnection;

public sealed class ServiceRuntimeContext
{
    public ServiceRuntimeContext(
        ApplicationStartupController controller,
        ApplicationStartupSession session,
        IPData client,
        LicenseObservationSnapshot entitlement,
        ApiConfigurationResponse configuration,
        ApiServiceConfigurationResponse serviceConfiguration,
        PointStatusRefreshService pointStatusRefresh,
        ComponentVersionStatusService componentVersionStatus)
    {
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Client = client ?? throw new ArgumentNullException(nameof(client));
        Entitlement = entitlement ?? throw new ArgumentNullException(nameof(entitlement));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        ServiceConfiguration = serviceConfiguration ?? throw new ArgumentNullException(nameof(serviceConfiguration));
        PointStatusRefresh = pointStatusRefresh ?? throw new ArgumentNullException(nameof(pointStatusRefresh));
        ComponentVersionStatus = componentVersionStatus ?? throw new ArgumentNullException(nameof(componentVersionStatus));

        if (ServiceEntitlementEvaluator.Evaluate(entitlement) != ServiceConnectionState.Active)
            throw new ArgumentException("Service entitlement is not active.", nameof(entitlement));
        if (!string.Equals(client.ClientId, entitlement.ClientId, StringComparison.Ordinal) ||
            !string.Equals(configuration.Client?.ClientId, entitlement.ClientId, StringComparison.Ordinal) ||
            !string.Equals(configuration.Device?.DeviceId, entitlement.DeviceId, StringComparison.Ordinal) ||
            !string.Equals(serviceConfiguration.ClientId, entitlement.ClientId, StringComparison.Ordinal) ||
            !string.Equals(serviceConfiguration.DeviceId, entitlement.DeviceId, StringComparison.Ordinal))
            throw new ArgumentException("Service runtime identities do not match the validated entitlement.");
    }

    public ApplicationStartupController Controller { get; }
    public ApplicationStartupSession Session { get; }
    public StartupResult Startup => Session.Startup;
    public IPData Client { get; }
    public string DeviceId => Entitlement.DeviceId;
    public LicenseObservationSnapshot Entitlement { get; internal set; }
    public ApiConfigurationResponse Configuration { get; }
    // Service-only secrets are not part of ordinary configuration/current and
    // are deliberately not copied into IPData or API session state.
    public ApiServiceConfigurationResponse ServiceConfiguration { get; }
    public string? IdentificationCode => ServiceConfiguration.IdentificationCode;
    public string? ChzToken => ServiceConfiguration.ChzToken;
    public VersionsData? EffectiveVersions => Client.Versions ?? Startup.RemoteVersions;
    public PointStatusRefreshService PointStatusRefresh { get; }
    public ComponentVersionStatusService ComponentVersionStatus { get; }
}
