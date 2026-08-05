using HonestFlow.Application.Licensing;
using HonestFlow.Models;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class LicenseStatusRefreshTests
    {
        [Fact]
        public void SnapshotForSelectedClient_MatchesOnlyCurrentClient()
        {
            var service = new LicensePresentationService();
            var selected = new IPData { ClientId = "client-a" };

            Assert.True(service.IsForClient(selected, new LicenseObservationSnapshot { ClientId = "client-a" }));
            Assert.False(service.IsForClient(selected, new LicenseObservationSnapshot { ClientId = "client-b" }));
            Assert.False(service.IsForClient(selected, null));
        }
    }
}
