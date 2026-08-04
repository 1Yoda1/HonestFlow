using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.PointStatus
{
    public sealed class WindowsServiceSnapshotProvider : IWindowsServiceSnapshotProvider
    {
        private static readonly string[] KnownServiceNames =
        {
            "esm-lm-controller",
            "uem-agent",
            "uem-updater",
            "atol-grpc-service",
            "esm-orchestrator",
            "regime",
            "yenisei"
        };

        private readonly EsmCmServiceNameResolver _esmCmResolver;

        public WindowsServiceSnapshotProvider(EsmCmServiceNameResolver esmCmResolver = null)
        {
            _esmCmResolver = esmCmResolver ?? new EsmCmServiceNameResolver();
        }

        public Task<ServiceSnapshot[]> GetSnapshotsAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() => GetSnapshots(cancellationToken), cancellationToken);
        }

        private ServiceSnapshot[] GetSnapshots(CancellationToken cancellationToken)
        {
            var snapshots = new List<ServiceSnapshot>();
            foreach (string serviceName in KnownServiceNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ServiceSnapshot snapshot = TryGetService(serviceName);
                if (snapshot != null)
                    snapshots.Add(snapshot);
            }

            string esmCmName = _esmCmResolver.ResolveFromLogs();
            ServiceSnapshot esmCm = EsmCmServiceNameResolver.IsValidServiceName(esmCmName)
                ? TryGetService(esmCmName)
                : null;

            // On first launch the service may exist before its log file is created.
            // Avoid enumerating every Windows service when ESM itself is absent.
            bool esmInstalled = snapshots.Any(snapshot =>
                string.Equals(
                    snapshot.ServiceName,
                    "esm-orchestrator",
                    StringComparison.OrdinalIgnoreCase));
            if (esmCm == null && esmInstalled)
                esmCm = FindRegisteredEsmCmService(cancellationToken);
            if (esmCm != null)
                snapshots.Add(esmCm);

            return snapshots.ToArray();
        }

        private static ServiceSnapshot TryGetService(string serviceName)
        {
            try
            {
                using var service = new ServiceController(serviceName);
                return new ServiceSnapshot(service.ServiceName, service.Status.ToString());
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static ServiceSnapshot FindRegisteredEsmCmService(CancellationToken cancellationToken)
        {
            foreach (ServiceController service in ServiceController.GetServices())
            {
                using (service)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (EsmCmServiceNameResolver.IsValidServiceName(service.ServiceName))
                        return new ServiceSnapshot(service.ServiceName, service.Status.ToString());
                }
            }

            return null;
        }
    }
}
