using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Api;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class ApiSessionStoreTests
    {
        [Fact]
        public async Task ProtectedSession_RoundTripsWithoutPlaintextTokens()
        {
            string directory = Path.Combine(Path.GetTempPath(), "HonestFlowTests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "api-session.dpapi");
            try
            {
                var store = new FileApiSessionStore(path, new ReversingProtector());
                var expected = new ApiSession
                {
                    AccessToken = "secret-access-token",
                    RefreshToken = "secret-refresh-token",
                    AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
                };
                await store.SaveAsync(expected, CancellationToken.None);

                string persisted = await File.ReadAllTextAsync(path);
                Assert.DoesNotContain(expected.AccessToken, persisted);
                Assert.DoesNotContain(expected.RefreshToken, persisted);
                ApiSession actual = await store.LoadAsync(CancellationToken.None);
                Assert.Equal(expected.AccessToken, actual.AccessToken);
                Assert.Equal(expected.RefreshToken, actual.RefreshToken);
                Assert.Equal(expected.AccessTokenExpiresAtUtc, actual.AccessTokenExpiresAtUtc);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private sealed class ReversingProtector : IApiSessionProtector
        {
            public byte[] Protect(byte[] plaintext) => Transform(plaintext);
            public byte[] Unprotect(byte[] protectedData) => Transform(protectedData);
            private static byte[] Transform(byte[] value)
            {
                byte[] copy = (byte[])value.Clone();
                Array.Reverse(copy);
                return copy;
            }
        }
    }
}
