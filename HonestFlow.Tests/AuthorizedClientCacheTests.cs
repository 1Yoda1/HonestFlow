using System;
using System.IO;
using System.Text;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Models;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class AuthorizedClientCacheTests
    {
        [Fact]
        public void SaveAndLoad_PreservesOnlyLastAuthorizedClient()
        {
            string path = TemporaryPath();
            try
            {
                var cache = new AuthorizedClientCache(path);
                cache.Save(Client("client-one", "1111", "token-one"));
                cache.Save(Client("client-two", "2222", "token-two"));

                IPData loaded = cache.Load();

                Assert.Equal("client-two", loaded.ClientId);
                Assert.Equal("2222", loaded.Password);
                Assert.Equal("token-two", loaded.Token);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void SavedFile_DoesNotContainPlaintextSecrets()
        {
            string path = TemporaryPath();
            try
            {
                var cache = new AuthorizedClientCache(path);
                cache.Save(Client("client-secret-marker", "4827", "lm-token-secret-marker"));
                byte[] encrypted = File.ReadAllBytes(path);

                Assert.False(Contains(encrypted, Encoding.UTF8.GetBytes("client-secret-marker")));
                Assert.False(Contains(encrypted, Encoding.UTF8.GetBytes("4827")));
                Assert.False(Contains(encrypted, Encoding.UTF8.GetBytes("lm-token-secret-marker")));
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static IPData Client(string id, string password, string token) => new()
        {
            ClientId = id,
            Name = id,
            Password = password,
            Token = token,
            Inn = "1234567890",
            RuDesktop = new RuDesktopOptions { Password = "remote-secret-marker" }
        };

        private static string TemporaryPath() =>
            Path.Combine(Path.GetTempPath(), "HonestFlow.Tests", Guid.NewGuid().ToString("N") + ".dpapi");

        private static bool Contains(byte[] source, byte[] value)
        {
            if (source == null || value == null || value.Length == 0 || source.Length < value.Length)
                return false;

            for (int i = 0; i <= source.Length - value.Length; i++)
            {
                int j = 0;
                while (j < value.Length && source[i + j] == value[j])
                    j++;
                if (j == value.Length)
                    return true;
            }

            return false;
        }
    }
}
