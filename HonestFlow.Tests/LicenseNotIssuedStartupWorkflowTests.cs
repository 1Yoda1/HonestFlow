using System;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Licensing;
using HonestFlow.Models;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class LicenseNotIssuedStartupWorkflowTests
    {
        [Fact]
        public async Task Check_LicenseStillMissing_RemainsRestrictedAndOnlyRefreshesLicense()
        {
            var refresher = new FakeLicenseRefresher
            {
                Result = new LicenseObservationSnapshot
                {
                    Decision = LicenseDecision.LicenseNotIssued
                }
            };
            var workflow = new LicenseNotIssuedStartupWorkflow(
                refresher,
                new IPData { ClientId = "client-1" });

            LicenseNotIssuedStartupResult result = await workflow.CheckAsync(CancellationToken.None);

            Assert.False(result.IsAllowed);
            Assert.Equal(LicenseDecision.LicenseNotIssued, result.Snapshot.Decision);
            Assert.Equal(LicenseNotIssuedStartupWorkflow.WaitingMessage, result.Message);
            Assert.Equal(1, refresher.Calls);
        }

        [Fact]
        public async Task Check_LicenseBecomesAllowed_ContinuesWithExistingClient()
        {
            var client = new IPData { ClientId = "client-1", Name = "Point" };
            var refresher = new FakeLicenseRefresher
            {
                Result = new LicenseObservationSnapshot
                {
                    ClientId = "client-1",
                    DeviceId = "device-1",
                    Decision = LicenseDecision.Allowed
                }
            };
            var workflow = new LicenseNotIssuedStartupWorkflow(refresher, client);

            LicenseNotIssuedStartupResult result = await workflow.CheckAsync(CancellationToken.None);

            Assert.True(result.IsAllowed);
            Assert.Same(client, result.Authentication.Client);
            Assert.Equal(LicenseDecision.Allowed, result.Authentication.LicenseSnapshot.Decision);
            Assert.Equal(1, refresher.Calls);
        }

        private sealed class FakeLicenseRefresher : ILicenseObservationRefresher
        {
            public LicenseObservationSnapshot Result { get; set; }
            public int Calls { get; private set; }

            public Task<LicenseObservationSnapshot> RefreshLicenseAsync(
                IPData client,
                IProgress<LicenseAuthenticationProgress> progress,
                CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(Result);
            }
        }
    }
}
