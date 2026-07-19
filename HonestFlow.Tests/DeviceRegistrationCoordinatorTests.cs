using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using Newtonsoft.Json;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class DeviceRegistrationCoordinatorTests
    {
        [Fact]
        public async Task TrySend_SendsAndMarksRequestOnlyOnce()
        {
            var sender = new FakeSender();
            var state = new FakeStateStore();
            var coordinator = CreateCoordinator(sender, state);
            LicenseObservationSnapshot snapshot = UnregisteredSnapshot();

            DeviceRegistrationDeliveryStatus first = await coordinator.TrySendAsync(
                snapshot, "PC", "ул. Ленина, 10", "2.4.2.0", CancellationToken.None);
            DeviceRegistrationDeliveryStatus second = await coordinator.TrySendAsync(
                snapshot, "PC", "2.4.2.0", CancellationToken.None);

            Assert.Equal(DeviceRegistrationDeliveryStatus.Sent, first);
            Assert.Equal(DeviceRegistrationDeliveryStatus.AlreadySent, second);
            Assert.Equal(1, sender.SendCalls);
            Assert.Equal(1, state.MarkCalls);
            Assert.DoesNotContain("password", sender.LastRequest ?? string.Empty);
            Assert.Equal(
                "ул. Ленина, 10",
                JsonConvert.DeserializeObject<DeviceRegistrationRequest>(sender.LastRequest).Address);
        }

        [Fact]
        public async Task TrySend_DoesNotMarkFailedDelivery()
        {
            var sender = new FakeSender { Fail = true };
            var state = new FakeStateStore();
            var coordinator = CreateCoordinator(sender, state);

            DeviceRegistrationDeliveryStatus result = await coordinator.TrySendAsync(
                UnregisteredSnapshot(), "PC", "2.4.2.0", CancellationToken.None);

            Assert.Equal(DeviceRegistrationDeliveryStatus.Failed, result);
            Assert.Equal(0, state.MarkCalls);
        }

        [Fact]
        public async Task TrySend_IgnoresOtherLicenseDecisions()
        {
            var sender = new FakeSender();
            var coordinator = CreateCoordinator(sender, new FakeStateStore());
            var snapshot = UnregisteredSnapshot();
            snapshot.Decision = LicenseDecision.Allowed;

            DeviceRegistrationDeliveryStatus result = await coordinator.TrySendAsync(
                snapshot, "PC", "2.4.2.0", CancellationToken.None);

            Assert.Equal(DeviceRegistrationDeliveryStatus.NotApplicable, result);
            Assert.Equal(0, sender.SendCalls);
        }

        private static DeviceRegistrationCoordinator CreateCoordinator(
            IDeviceRegistrationRequestSender sender,
            IDeviceRegistrationDeliveryStateStore state) =>
            new(new DeviceRegistrationRequestService(), sender, state);

        private static LicenseObservationSnapshot UnregisteredSnapshot() => new()
        {
            Decision = LicenseDecision.DeviceNotRegistered,
            ClientId = "client-1",
            DeviceId = "device-1"
        };

        private sealed class FakeSender : IDeviceRegistrationRequestSender
        {
            public bool Fail { get; set; }
            public int SendCalls { get; private set; }
            public string LastRequest { get; private set; }

            public Task SendAsync(string requestJson, CancellationToken cancellationToken)
            {
                SendCalls++;
                LastRequest = requestJson;
                if (Fail)
                    throw new System.InvalidOperationException("simulated");
                return Task.CompletedTask;
            }
        }

        private sealed class FakeStateStore : IDeviceRegistrationDeliveryStateStore
        {
            private readonly HashSet<string> _sent = new();
            public int MarkCalls { get; private set; }

            public Task<bool> WasSentAsync(
                string clientId,
                string deviceId,
                CancellationToken cancellationToken) =>
                Task.FromResult(_sent.Contains(clientId + "/" + deviceId));

            public Task MarkSentAsync(
                string clientId,
                string deviceId,
                CancellationToken cancellationToken)
            {
                MarkCalls++;
                _sent.Add(clientId + "/" + deviceId);
                return Task.CompletedTask;
            }
        }
    }
}
