using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Models;
using Newtonsoft.Json;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class DeviceRegistrationStartupWorkflowTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Submit_EmptyAddressDoesNotSend(string address)
        {
            var sender = new FakeSender();
            var workflow = CreateWorkflow(sender, new FakeStatusProvider());

            DeviceRegistrationStartupResult result = await workflow.SubmitAsync(
                Snapshot(), address, CancellationToken.None);

            Assert.Equal(DeviceRegistrationStartupState.InvalidAddress, result.State);
            Assert.Equal(0, sender.SendCalls);
        }

        [Fact]
        public async Task Submit_AddressLongerThan300DoesNotSend()
        {
            var sender = new FakeSender();
            var workflow = CreateWorkflow(sender, new FakeStatusProvider());

            DeviceRegistrationStartupResult result = await workflow.SubmitAsync(
                Snapshot(), new string('a', 301), CancellationToken.None);

            Assert.Equal(DeviceRegistrationStartupState.InvalidAddress, result.State);
            Assert.Equal(0, sender.SendCalls);
        }

        [Fact]
        public async Task Submit_UsesMachineNameAndSendsTrimmedAddress()
        {
            var sender = new FakeSender();
            var workflow = CreateWorkflow(sender, new FakeStatusProvider());

            DeviceRegistrationStartupResult result = await workflow.SubmitAsync(
                Snapshot(), "  ул. Ленина, 10  ", CancellationToken.None);

            DeviceRegistrationRequest request = JsonConvert.DeserializeObject<DeviceRegistrationRequest>(sender.LastRequest);
            Assert.Equal(DeviceRegistrationStartupState.Pending, result.State);
            Assert.Equal(Environment.MachineName, request.DeviceName);
            Assert.Equal("ул. Ленина, 10", request.Address);
            Assert.Equal("device-1", request.DeviceId);
            Assert.Equal("client-1", request.ClientId);
        }

        [Fact]
        public async Task Check_PendingKeepsRestrictedState()
        {
            var refresher = new FakeSessionRefresher();
            var authentication = new FakeAuthentication();
            var workflow = CreateWorkflow(
                new FakeSender(),
                new FakeStatusProvider { Current = new DeviceRegistrationStatus { Status = "Pending" } },
                refresher,
                authentication);

            DeviceRegistrationStartupResult result = await workflow.CheckAsync(CancellationToken.None);

            Assert.Equal(DeviceRegistrationStartupState.Pending, result.State);
            Assert.Equal(0, refresher.RefreshCalls);
            Assert.Equal(0, authentication.ResumeCalls);
        }

        [Fact]
        public async Task Check_ApprovedRefreshesAndContinuesWhenLicenseAllows()
        {
            var refresher = new FakeSessionRefresher { RefreshResult = true };
            var authentication = new FakeAuthentication
            {
                ResumeResult = new LicenseAuthenticationResult(
                    new IPData { ClientId = "client-1", Name = "Point" },
                    new LicenseObservationSnapshot { Decision = LicenseDecision.Allowed })
            };
            var workflow = CreateWorkflow(
                new FakeSender(),
                new FakeStatusProvider { Current = new DeviceRegistrationStatus { Status = "Approved" } },
                refresher,
                authentication);

            DeviceRegistrationStartupResult result = await workflow.CheckAsync(CancellationToken.None);

            Assert.Equal(DeviceRegistrationStartupState.Allowed, result.State);
            Assert.Equal("client-1", result.Authentication.Client.ClientId);
            Assert.Equal(1, refresher.RefreshCalls);
            Assert.Equal(1, authentication.ResumeCalls);
        }

        [Fact]
        public async Task Check_ApprovedWithoutLicenseRemainsRetryable()
        {
            var refresher = new FakeSessionRefresher { RefreshResult = true };
            var authentication = new FakeAuthentication
            {
                ResumeResult = new LicenseAuthenticationResult(
                    new IPData { ClientId = "client-1" },
                    new LicenseObservationSnapshot
                    {
                        Decision = LicenseDecision.DeviceNotRegistered,
                        Message = "Лицензия ещё не опубликована."
                    })
            };
            var workflow = CreateWorkflow(
                new FakeSender(),
                new FakeStatusProvider { Current = new DeviceRegistrationStatus { Status = "Approved" } },
                refresher,
                authentication);

            DeviceRegistrationStartupResult result = await workflow.CheckAsync(CancellationToken.None);

            Assert.Equal(DeviceRegistrationStartupState.ApprovedNotReady, result.State);
            Assert.Equal("Лицензия ещё не опубликована.", result.Message);
            Assert.Equal(1, refresher.RefreshCalls);
            Assert.Equal(1, authentication.ResumeCalls);
        }

        [Fact]
        public async Task Check_RejectedDoesNotContinueStartup()
        {
            var refresher = new FakeSessionRefresher();
            var authentication = new FakeAuthentication();
            var workflow = CreateWorkflow(
                new FakeSender(),
                new FakeStatusProvider
                {
                    Current = new DeviceRegistrationStatus { Status = "Rejected", Comment = "Адрес не подтверждён." }
                },
                refresher,
                authentication);

            DeviceRegistrationStartupResult result = await workflow.CheckAsync(CancellationToken.None);

            Assert.Equal(DeviceRegistrationStartupState.Rejected, result.State);
            Assert.Contains("Адрес не подтверждён.", result.Message);
            Assert.Equal(0, refresher.RefreshCalls);
            Assert.Equal(0, authentication.ResumeCalls);
        }

        private static DeviceRegistrationStartupWorkflow CreateWorkflow(
            FakeSender sender,
            IDeviceRegistrationStatusProvider statusProvider,
            IApiSessionRefresher refresher = null,
            IApiCredentialAuthService authentication = null)
        {
            var registration = new DeviceRegistrationWorkflow(new DeviceRegistrationCoordinator(
                new DeviceRegistrationRequestService(), sender, new FakeStateStore()));
            return new DeviceRegistrationStartupWorkflow(
                registration,
                statusProvider,
                refresher ?? new FakeSessionRefresher(),
                authentication ?? new FakeAuthentication());
        }

        private static LicenseObservationSnapshot Snapshot() => new()
        {
            Decision = LicenseDecision.DeviceNotRegistered,
            ClientId = "client-1",
            DeviceId = "device-1"
        };

        private sealed class FakeSender : IDeviceRegistrationRequestSender
        {
            public int SendCalls { get; private set; }
            public string LastRequest { get; private set; }

            public Task SendAsync(string requestJson, CancellationToken cancellationToken)
            {
                SendCalls++;
                LastRequest = requestJson;
                return Task.CompletedTask;
            }
        }

        private sealed class FakeStateStore : IDeviceRegistrationDeliveryStateStore
        {
            private readonly HashSet<string> _sent = new();

            public Task<bool> WasSentAsync(string clientId, string deviceId, CancellationToken cancellationToken) =>
                Task.FromResult(_sent.Contains(clientId + "/" + deviceId));

            public Task MarkSentAsync(string clientId, string deviceId, CancellationToken cancellationToken)
            {
                _sent.Add(clientId + "/" + deviceId);
                return Task.CompletedTask;
            }
        }

        private sealed class FakeStatusProvider : IDeviceRegistrationStatusProvider
        {
            public DeviceRegistrationStatus Current { get; set; }
            public Task<DeviceRegistrationStatus> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(Current);
        }

        private sealed class FakeSessionRefresher : IApiSessionRefresher
        {
            public bool RefreshResult { get; set; }
            public int RefreshCalls { get; private set; }

            public Task<bool> RefreshSessionAsync(CancellationToken cancellationToken)
            {
                RefreshCalls++;
                return Task.FromResult(RefreshResult);
            }
        }

        private sealed class FakeAuthentication : IApiCredentialAuthService
        {
            public LicenseAuthenticationResult ResumeResult { get; set; }
            public int ResumeCalls { get; private set; }

            public void LoadIpList() { }
            public IPData Authenticate(string password) => null;
            public Task<LicenseAuthenticationResult> AuthenticateAsync(string login, string password, IProgress<LicenseAuthenticationProgress> progress, CancellationToken cancellationToken) =>
                Task.FromResult(new LicenseAuthenticationResult(null, null));
            public Task<LicenseAuthenticationResult> TryResumeAsync(IProgress<LicenseAuthenticationProgress> progress, CancellationToken cancellationToken)
            {
                ResumeCalls++;
                return Task.FromResult(ResumeResult);
            }
        }
    }
}
