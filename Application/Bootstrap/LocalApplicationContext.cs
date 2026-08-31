using System;
using HonestFlow.Application.Core;
using HonestFlow.Application.Diagnostics;
using HonestFlow.Application.Installation;
using HonestFlow.Application.PointStatus;

namespace HonestFlow.Application.Bootstrap;

public sealed class LocalApplicationContext
{
    public LocalApplicationContext(
        ILogService logService,
        LocalRuntimeContext runtime,
        PointStatusRefreshService pointStatusRefresh,
        ComponentVersionStatusService componentVersionStatus,
        DiagnosticArchiveService diagnosticArchive,
        WindowsServiceSnapshotProvider serviceSnapshotProvider)
    {
        LogService = logService ?? throw new ArgumentNullException(nameof(logService));
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        PointStatusRefresh = pointStatusRefresh ?? throw new ArgumentNullException(nameof(pointStatusRefresh));
        ComponentVersionStatus = componentVersionStatus ?? throw new ArgumentNullException(nameof(componentVersionStatus));
        DiagnosticArchive = diagnosticArchive ?? throw new ArgumentNullException(nameof(diagnosticArchive));
        ServiceSnapshotProvider = serviceSnapshotProvider ?? throw new ArgumentNullException(nameof(serviceSnapshotProvider));
    }

    public ApplicationMode Mode => ApplicationMode.Free;
    public ILogService LogService { get; }
    public LocalRuntimeContext Runtime { get; }
    public PointStatusRefreshService PointStatusRefresh { get; }
    public ComponentVersionStatusService ComponentVersionStatus { get; }
    public DiagnosticArchiveService DiagnosticArchive { get; }
    public WindowsServiceSnapshotProvider ServiceSnapshotProvider { get; }
}
