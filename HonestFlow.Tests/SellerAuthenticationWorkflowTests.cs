using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Licensing;
using HonestFlow.Models;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class SellerAuthenticationWorkflowTests
    {
        [Fact]
        public async Task AuthenticateAsync_WrapsLegacyAuthResultWithCurrentSnapshot()
        {
            var client = new IPData { ClientId = "client-1" };
            var auth = new StubAuthService(client);
            var store = new StubSnapshotStore
            {
                Current = new LicenseObservationSnapshot { ClientId = "client-1" }
            };
            var workflow = new SellerAuthenticationWorkflow(auth, store);

            LicenseAuthenticationResult result = await workflow.AuthenticateAsync(
                "1234",
                null,
                CancellationToken.None);

            Assert.False(workflow.ReportsLicenseProgress);
            Assert.Same(client, result.Client);
            Assert.Same(store.Current, result.LicenseSnapshot);
        }

        private sealed class StubAuthService : IAuthService
        {
            private readonly IPData _client;
            public StubAuthService(IPData client) => _client = client;
            public void LoadIpList() { }
            public IPData Authenticate(string password) => _client;
        }

        private sealed class StubSnapshotStore : ILicenseObservationSnapshotStore
        {
            public LicenseObservationSnapshot Current { get; set; }
            public event System.Action<LicenseObservationSnapshot> SnapshotChanged { add { } remove { } }
            public void Set(LicenseObservationSnapshot snapshot) => Current = snapshot;
        }
    }
}
