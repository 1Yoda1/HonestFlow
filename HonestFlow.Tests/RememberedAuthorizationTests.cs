using System.Security.Cryptography;
using System.Text;
using HonestFlow.Application.RemoteAccess;
using HonestFlow.Models;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class RememberedAuthorizationTests
    {
        [Fact]
        public void RememberedAuthorization_IsCurrentOnlyForSameClientAndPassword()
        {
            var remembered = new LastAuthorizedClientState
            {
                ClientId = "client-a",
                AuthorizationPasswordFingerprint = Fingerprint("1234")
            };

            Assert.True(RuDesktopService.IsRememberedAuthorizationCurrent(
                remembered,
                new IPData { ClientId = "client-a", Password = "1234" }));
            Assert.False(RuDesktopService.IsRememberedAuthorizationCurrent(
                remembered,
                new IPData { ClientId = "client-a", Password = "5678" }));
            Assert.False(RuDesktopService.IsRememberedAuthorizationCurrent(
                remembered,
                new IPData { ClientId = "client-b", Password = "1234" }));
        }

        [Fact]
        public void RememberedAuthorization_RejectsLegacyStateWithoutPasswordFingerprint()
        {
            Assert.False(RuDesktopService.IsRememberedAuthorizationCurrent(
                new LastAuthorizedClientState { ClientId = "client-a" },
                new IPData { ClientId = "client-a", Password = "1234" }));
        }

        private static string Fingerprint(string password)
        {
            return System.Convert.ToBase64String(
                SHA256.HashData(Encoding.UTF8.GetBytes(password)));
        }
    }
}
