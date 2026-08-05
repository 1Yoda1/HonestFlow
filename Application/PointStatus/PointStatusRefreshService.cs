using System;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Installation;
using HonestFlow.Models;

namespace HonestFlow.Application.PointStatus
{
    public sealed class PointStatusRefreshService
    {
        private readonly IPointStatusService _pointStatusService;
        private readonly ComponentVersionStatusService _componentVersionStatusService;
        private readonly PointStatusReportBuilder _reportBuilder;

        public PointStatusRefreshService(
            IPointStatusService pointStatusService,
            ComponentVersionStatusService componentVersionStatusService,
            PointStatusReportBuilder reportBuilder)
        {
            _pointStatusService = pointStatusService ?? throw new ArgumentNullException(nameof(pointStatusService));
            _componentVersionStatusService = componentVersionStatusService ?? throw new ArgumentNullException(nameof(componentVersionStatusService));
            _reportBuilder = reportBuilder ?? throw new ArgumentNullException(nameof(reportBuilder));
        }

        public async Task<PointStatusRefreshResult> RefreshAsync(
            IPData selectedClient,
            VersionsData configuredVersions,
            bool includeLicensedComponents,
            CancellationToken cancellationToken)
        {
            Task<PointStatusResult> pointStatusTask = _pointStatusService.CheckAsync(cancellationToken);
            Task<ComponentVersionStatus[]> versionStatusTask = includeLicensedComponents
                ? Task.Run(
                    () => _componentVersionStatusService.GetStatuses(selectedClient, configuredVersions),
                    cancellationToken)
                : Task.FromResult(Array.Empty<ComponentVersionStatus>());

            await Task.WhenAll(pointStatusTask, versionStatusTask);
            PointStatusResult pointStatus = await pointStatusTask;
            ComponentVersionStatus[] versionStatuses = await versionStatusTask;

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

            return new PointStatusRefreshResult(
                pointStatus,
                versionStatuses,
                _reportBuilder.Build(pointStatus),
                GetOverallLevel(visibleStatuses));
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
            NodeLevel overallLevel)
        {
            PointStatus = pointStatus;
            VersionStatuses = versionStatuses ?? Array.Empty<ComponentVersionStatus>();
            DiagnosticReport = diagnosticReport;
            OverallLevel = overallLevel;
        }

        public PointStatusResult PointStatus { get; }
        public ComponentVersionStatus[] VersionStatuses { get; }
        public string DiagnosticReport { get; }
        public NodeLevel OverallLevel { get; }
    }
}
