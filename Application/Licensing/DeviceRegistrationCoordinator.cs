using System;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Infrastructure;

namespace HonestFlow.Application.Licensing
{
    public sealed class DeviceRegistrationCoordinator
    {
        private readonly DeviceRegistrationRequestService _requestService;
        private readonly IDeviceRegistrationRequestSender _sender;
        private readonly IDeviceRegistrationDeliveryStateStore _stateStore;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public DeviceRegistrationCoordinator(
            DeviceRegistrationRequestService requestService,
            IDeviceRegistrationRequestSender sender,
            IDeviceRegistrationDeliveryStateStore stateStore)
        {
            _requestService = requestService ?? throw new ArgumentNullException(nameof(requestService));
            _sender = sender ?? throw new ArgumentNullException(nameof(sender));
            _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        }

        public async Task<DeviceRegistrationDeliveryStatus> TrySendAsync(
            LicenseObservationSnapshot snapshot,
            string deviceName,
            string honestFlowVersion,
            CancellationToken cancellationToken)
        {
            return await TrySendAsync(
                snapshot,
                deviceName,
                null,
                honestFlowVersion,
                cancellationToken);
        }

        public async Task<DeviceRegistrationDeliveryStatus> TrySendAsync(
            LicenseObservationSnapshot snapshot,
            string deviceName,
            string pointAddress,
            string honestFlowVersion,
            CancellationToken cancellationToken)
        {
            if (snapshot == null ||
                snapshot.Decision != LicenseDecision.DeviceNotRegistered ||
                string.IsNullOrWhiteSpace(snapshot.ClientId) ||
                string.IsNullOrWhiteSpace(snapshot.DeviceId))
            {
                return DeviceRegistrationDeliveryStatus.NotApplicable;
            }

            try
            {
                await _gate.WaitAsync(cancellationToken);
                try
                {
                    if (await _stateStore.WasSentAsync(
                        snapshot.ClientId,
                        snapshot.DeviceId,
                        cancellationToken))
                    {
                        Logger.Info(
                            "Event=DeviceRegistrationRequestDelivery Status=AlreadySent",
                            nameof(DeviceRegistrationCoordinator));
                        return DeviceRegistrationDeliveryStatus.AlreadySent;
                    }

                    string request = _requestService.Create(
                        snapshot.ClientId,
                        snapshot.DeviceId,
                        deviceName,
                        pointAddress,
                        honestFlowVersion,
                        DateTimeOffset.UtcNow);
                    await _sender.SendAsync(request, cancellationToken);
                    await _stateStore.MarkSentAsync(
                        snapshot.ClientId,
                        snapshot.DeviceId,
                        cancellationToken);
                    Logger.Info(
                        "Event=DeviceRegistrationRequestDelivery Status=Sent",
                        nameof(DeviceRegistrationCoordinator));
                    return DeviceRegistrationDeliveryStatus.Sent;
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warning(
                    $"Event=DeviceRegistrationRequestDelivery Status=Failed ErrorType={ex.GetType().Name}",
                    nameof(DeviceRegistrationCoordinator));
                return DeviceRegistrationDeliveryStatus.Failed;
            }
        }
    }
}
