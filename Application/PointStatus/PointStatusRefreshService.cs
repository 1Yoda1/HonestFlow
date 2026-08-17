using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Core;
using HonestFlow.Application.Installation;
using HonestFlow.Models;

namespace HonestFlow.Application.PointStatus
{
    public sealed class PointStatusRefreshService
    {
        private readonly IPointStatusService _pointStatusService;
        private readonly ComponentVersionStatusService _componentVersionStatusService;
        private readonly PointStatusReportBuilder _reportBuilder;
        private readonly ILogService _log;

        public PointStatusRefreshService(
            IPointStatusService pointStatusService,
            ComponentVersionStatusService componentVersionStatusService,
            PointStatusReportBuilder reportBuilder,
            ILogService log = null)
        {
            _pointStatusService = pointStatusService ?? throw new ArgumentNullException(nameof(pointStatusService));
            _componentVersionStatusService = componentVersionStatusService ?? throw new ArgumentNullException(nameof(componentVersionStatusService));
            _reportBuilder = reportBuilder ?? throw new ArgumentNullException(nameof(reportBuilder));
            _log = log;
        }

        public async Task<PointStatusRefreshResult> RefreshAsync(
            IPData selectedClient,
            VersionsData configuredVersions,
            bool includeLicensedComponents,
            CancellationToken cancellationToken)
        {
            Task<PointStatusResult> pointStatusTask = _pointStatusService.CheckAsync(selectedClient, cancellationToken);
            Task<ComponentVersionStatus[]> versionStatusTask = includeLicensedComponents
                ? Task.Run(
                    () => _componentVersionStatusService.GetStatuses(selectedClient, configuredVersions),
                    cancellationToken)
                : Task.FromResult(Array.Empty<ComponentVersionStatus>());

            await Task.WhenAll(pointStatusTask, versionStatusTask);
            PointStatusResult pointStatus = await pointStatusTask;
            ComponentVersionStatus[] versionStatuses = await versionStatusTask;

            if (pointStatus.ControllerServiceStatus != null || pointStatus.ControllerServiceInfo != null)
            {
                ComponentVersionStatus controllerVersion = versionStatuses.FirstOrDefault(status =>
                    string.Equals(status.ComponentName, "Контроллер", StringComparison.OrdinalIgnoreCase));
                pointStatus.Controller = PointStatusService.BuildControllerStatus(
                    pointStatus.ControllerServiceStatus,
                    pointStatus.ControllerServiceInfo,
                    controllerVersion);
            }

            if (pointStatus.EsmServiceStatus != null || pointStatus.EsmApiPort != null || pointStatus.EsmRegistration != null)
            {
                ComponentVersionStatus esmVersion = versionStatuses.FirstOrDefault(status =>
                    string.Equals(status.ComponentName, "ЕСМ", StringComparison.OrdinalIgnoreCase));
                pointStatus.Esm = PointStatusService.BuildEsmStatus(
                    pointStatus.EsmServiceStatus,
                    pointStatus.EsmApiPort,
                    pointStatus.EsmRegistration,
                    esmVersion);
            }

            if (pointStatus.KktServiceStatus != null || pointStatus.KktDriver != null || pointStatus.KktPort4041 != null)
            {
                ComponentVersionStatus kktDriverVersion = versionStatuses.FirstOrDefault(status =>
                    string.Equals(status.ComponentName, "Драйвер ККТ", StringComparison.OrdinalIgnoreCase));
                pointStatus.Kkt = PointStatusService.BuildKktStatus(
                    pointStatus.KktPnP,
                    pointStatus.KktDriver,
                    pointStatus.KktServiceStatus,
                    pointStatus.KktPort4041,
                    kktDriverVersion);
            }

            NodeStatus[] visibleStatuses = includeLicensedComponents
                ? new[]
                {
                    pointStatus.Lm,
                    pointStatus.Controller,
                    pointStatus.Esm,
                    pointStatus.Kkt,
                    pointStatus.Cloud,
                    pointStatus.RuDesktop
                }
                : new[] { pointStatus.Cloud, pointStatus.RuDesktop };

            DiagnosticsSnapshot diagnostics = new DiagnosticsSnapshotBuilder().Create(pointStatus, versionStatuses);
            foreach (DiagnosticIssue issue in diagnostics.Issues)
                _log?.LogDebug(issue.ToStructuredLog());
            return new PointStatusRefreshResult(
                pointStatus,
                versionStatuses,
                _reportBuilder.Build(pointStatus),
                GetOverallLevel(visibleStatuses),
                diagnostics);
        }

        private static NodeLevel GetOverallLevel(NodeStatus[] statuses)
        {
            NodeLevel result = NodeLevel.Ok;
            foreach (NodeStatus status in statuses)
            {
                if (status?.Level == NodeLevel.Error)
                    return NodeLevel.Error;
                if (status?.Level == NodeLevel.Warning)
                    result = NodeLevel.Warning;
            }

            return result;
        }
    }

    public sealed class PointStatusRefreshResult
    {
        public PointStatusRefreshResult(
            PointStatusResult pointStatus,
            ComponentVersionStatus[] versionStatuses,
            string diagnosticReport,
            NodeLevel overallLevel,
            DiagnosticsSnapshot diagnostics)
        {
            PointStatus = pointStatus;
            VersionStatuses = versionStatuses ?? Array.Empty<ComponentVersionStatus>();
            DiagnosticReport = diagnosticReport;
            OverallLevel = overallLevel;
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public PointStatusResult PointStatus { get; }
        public ComponentVersionStatus[] VersionStatuses { get; }
        public string DiagnosticReport { get; }
        public NodeLevel OverallLevel { get; }
        public DiagnosticsSnapshot Diagnostics { get; }
    }
}
