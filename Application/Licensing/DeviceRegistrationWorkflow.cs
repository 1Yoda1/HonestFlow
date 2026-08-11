using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Models;

namespace HonestFlow.Application.Licensing
{
    public sealed class DeviceRegistrationWorkflow
    {
        private readonly DeviceRegistrationCoordinator _coordinator;
        private readonly HashSet<string> _promptsShown = new(StringComparer.Ordinal);

        public DeviceRegistrationWorkflow(DeviceRegistrationCoordinator coordinator)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        }

        public async Task<DeviceRegistrationAction> EvaluateAsync(
            LicenseObservationSnapshot snapshot,
            IPData selectedClient,
            CancellationToken cancellationToken)
        {
            if (snapshot == null || selectedClient == null)
                return DeviceRegistrationAction.None;

            if (snapshot.Decision == LicenseDecision.DeviceNotRegistered)
            {
                if (await _coordinator.WasSentAsync(snapshot, cancellationToken))
                    return DeviceRegistrationAction.AlreadySent;

                string key = snapshot.ClientId + "/" + snapshot.DeviceId;
                return _promptsShown.Add(key)
                    ? DeviceRegistrationAction.RequestRegistration
                    : DeviceRegistrationAction.None;
            }

            return snapshot.Decision == LicenseDecision.Allowed &&
                   string.IsNullOrWhiteSpace(snapshot.PointAddress)
                ? DeviceRegistrationAction.SynchronizeAddress
                : DeviceRegistrationAction.None;
        }

        public Task<DeviceRegistrationDeliveryStatus> SendAsync(
            LicenseObservationSnapshot snapshot,
            string pointAddress,
            string honestFlowVersion,
            CancellationToken cancellationToken) =>
            _coordinator.TrySendAsync(
                snapshot,
                Environment.MachineName,
                pointAddress,
                honestFlowVersion,
                cancellationToken);

        public Task<DeviceRegistrationDeliveryStatus> SendExplicitAsync(
            LicenseObservationSnapshot snapshot,
            string pointAddress,
            string honestFlowVersion,
            CancellationToken cancellationToken) =>
            _coordinator.TrySendAsync(
                snapshot,
                Environment.MachineName,
                pointAddress,
                honestFlowVersion,
                true,
                cancellationToken);
    }

    public enum DeviceRegistrationAction
    {
        None,
        AlreadySent,
        RequestRegistration,
        SynchronizeAddress
    }
}
