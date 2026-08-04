using System.Reflection;
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
            MethodInfo method = typeof(MainForm).GetMethod(
                "IsLicenseSnapshotForSelectedClient",
                BindingFlags.NonPublic | BindingFlags.Static);
            var selected = new IPData { ClientId = "client-a" };

            Assert.True(Invoke(method, selected, new LicenseObservationSnapshot { ClientId = "client-a" }));
            Assert.False(Invoke(method, selected, new LicenseObservationSnapshot { ClientId = "client-b" }));
            Assert.False(Invoke(method, selected, null));
        }

        private static bool Invoke(
            MethodInfo method,
            IPData selected,
            LicenseObservationSnapshot snapshot)
        {
            return (bool)method.Invoke(null, new object[] { selected, snapshot });
        }
    }
}
