using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Licensing;
using HonestFlow.Models;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class LicenseObservingAuthServiceTests
    {
        [Fact]
        public async Task AuthenticateAsync_WaitsForLicenseObservation()
        {
            var client = new IPData { ClientId = "client-1", Name = "Client" };
            var snapshot = new LicenseObservationSnapshot
            {
                ClientId = client.ClientId,
                Decision = LicenseDecision.Allowed
            };
            var observer = new StubObserver(snapshot);
            var service = new LicenseObservingAuthService(new StubAuth(client), observer);
            var stages = new List<LicenseAuthenticationStage>();

            LicenseAuthenticationResult result = await service.AuthenticateAsync(
                "password",
                new InlineProgress(value => stages.Add(value.Stage)),
                CancellationToken.None);

            Assert.Same(client, result.Client);
            Assert.Same(snapshot, result.LicenseSnapshot);
            Assert.True(observer.WasCalled);
            Assert.Contains(LicenseAuthenticationStage.Completed, stages);
        }

        [Fact]
        public async Task AuthenticateAsync_WithWrongPassword_DoesNotObserveLicense()
        {
            var observer = new StubObserver(new LicenseObservationSnapshot());
            var service = new LicenseObservingAuthService(new StubAuth(null), observer);

            LicenseAuthenticationResult result = await service.AuthenticateAsync(
                "wrong",
                null,
                CancellationToken.None);

            Assert.Null(result.Client);
            Assert.Null(result.LicenseSnapshot);
            Assert.False(observer.WasCalled);
        }

        private sealed class StubAuth : IAuthService
        {
            private readonly IPData _client;
            public StubAuth(IPData client) => _client = client;
            public void LoadIpList() { }
            public IPData Authenticate(string password) => _client;
        }

        private sealed class StubObserver : ILicenseObservationService
        {
            private readonly LicenseObservationSnapshot _snapshot;
            public StubObserver(LicenseObservationSnapshot snapshot) => _snapshot = snapshot;
            public bool WasCalled { get; private set; }
            public Task<LicenseObservationSnapshot> ObserveAsync(
                IPData client,
                CancellationToken cancellationToken)
            {
                WasCalled = true;
                return Task.FromResult(_snapshot);
            }
        }

        private sealed class InlineProgress : IProgress<LicenseAuthenticationProgress>
        {
            private readonly Action<LicenseAuthenticationProgress> _report;
            public InlineProgress(Action<LicenseAuthenticationProgress> report) => _report = report;
            public void Report(LicenseAuthenticationProgress value) => _report(value);
        }
    }
}
