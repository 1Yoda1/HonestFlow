using System;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Core;
using HonestFlow.Application.DeviceIdentity;
using HonestFlow.Application.Diagnostics;
using HonestFlow.Application.Installation;
using HonestFlow.Application.PointStatus;
using HonestFlow.Application.RemoteAccess;

namespace HonestFlow.Application.Bootstrap;

/// <summary>
/// Composes the local-only Free runtime. It deliberately has no authentication,
/// session, device identity, remote configuration, or licensing dependency.
/// </summary>
public sealed class LocalApplicationBootstrap
{
    public LocalApplicationContext Create()
    {
        var log = new LogService();
        var runtime = LocalRuntimeContext.CreateCurrent();
        var versions = new ComponentVersionStatusService(log);
        var services = new WindowsServiceSnapshotProvider();
        var pointStatus = new PointStatusService(
            remoteConfigLoaded: false,
            ipCount: 0,
            clients: Array.Empty<Models.IPData>(),
            ruDesktopService: new RuDesktopService(log),
            serviceSnapshotProvider: services,
            cloudConnectivityProbe: new OfflineFreeCloudProbe());
        var refresh = new PointStatusRefreshService(
            pointStatus,
            versions,
            new PointStatusReportBuilder(),
            log);
        var archive = new DiagnosticArchiveService(log, new NoDeviceIdentityService());

        return new LocalApplicationContext(log, runtime, refresh, versions, archive, services);
    }

    private sealed class OfflineFreeCloudProbe : ICloudConnectivityProbe
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class NoDeviceIdentityService : IDeviceIdentityService
    {
        public Task<DeviceIdentityResult> GetOrCreateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(DeviceIdentityResult.Unavailable("FREE_MODE"));
    }
}
