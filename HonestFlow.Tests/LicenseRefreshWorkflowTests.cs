using System;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Licensing;
using HonestFlow.Models;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class LicenseRefreshWorkflowTests
    {
        [Fact]
        public async Task RefreshAsync_DelegatesToRefreshCapableAuthService()
        {
            var auth = new RefreshingAuthService();
            var workflow = new LicenseRefreshWorkflow(auth, null);
            var client = new IPData { ClientId = "client-1" };

            LicenseObservationSnapshot result = await workflow.RefreshAsync(
                client,
                null,
                CancellationToken.None);

            Assert.True(workflow.IsAvailable);
            Assert.Same(client, auth.RefreshedClient);
            Assert.Equal(LicenseDecision.Allowed, result.Decision);
        }

        [Fact]
        public async Task RefreshAsync_RejectsAuthServiceWithoutLicenseRefresh()
        {
            var workflow = new LicenseRefreshWorkflow(new BasicAuthService(), null);

            Assert.False(workflow.IsAvailable);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                workflow.RefreshAsync(new IPData(), null, CancellationToken.None));
        }

        private sealed class RefreshingAuthService : IAuthService, ILicenseObservationRefresher
        {
            public IPData RefreshedClient { get; private set; }
            public void LoadIpList() { }
            public IPData Authenticate(string password) => null;
            public Task<LicenseObservationSnapshot> RefreshLicenseAsync(
                IPData client,
                IProgress<LicenseAuthenticationProgress> progress,
                CancellationToken cancellationToken)
            {
                RefreshedClient = client;
                return Task.FromResult(new LicenseObservationSnapshot { Decision = LicenseDecision.Allowed });
            }
        }

        private sealed class BasicAuthService : IAuthService
        {
            public void LoadIpList() { }
            public IPData Authenticate(string password) => null;
        }
    }
}
