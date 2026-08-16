using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Configuration;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class LastAuthorizedClientHintStoreTests
    {
        [Fact]
        public async System.Threading.Tasks.Task SaveAndLoadForSameDevice_PreservesHistoricalClientOnly()
        {
            string path = TemporaryPath();
            try
            {
                var store = new LastAuthorizedClientHintStore(path);
                await store.SaveAsync(Hint("device-a", "client-a", "Клиент А"), CancellationToken.None);

                LastAuthorizedClientHint loaded = await store.LoadForDeviceAsync("device-a", CancellationToken.None);

                Assert.Equal("device-a", loaded.DeviceId);
                Assert.Equal("client-a", loaded.ClientId);
                Assert.Equal("Клиент А", loaded.ClientName);
                Assert.NotNull(loaded.SavedAtUtc);
            }
            finally
            {
                Delete(path);
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task LoadForDeviceAsync_DoesNotRevealHintForAnotherDevice()
        {
            string path = TemporaryPath();
            try
            {
                var store = new LastAuthorizedClientHintStore(path);
                await store.SaveAsync(Hint("device-a", "client-a", "Клиент А"), CancellationToken.None);

                LastAuthorizedClientHint loaded = await store.LoadForDeviceAsync("device-b", CancellationToken.None);

                Assert.Null(loaded);
            }
            finally
            {
                Delete(path);
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task SaveAsync_OverwritesPreviousHistoricalClient()
        {
            string path = TemporaryPath();
            try
            {
                var store = new LastAuthorizedClientHintStore(path);
                await store.SaveAsync(Hint("device-a", "client-a", "Клиент А"), CancellationToken.None);
                await store.SaveAsync(Hint("device-a", "client-b", "Клиент Б"), CancellationToken.None);

                LastAuthorizedClientHint loaded = await store.LoadForDeviceAsync("device-a", CancellationToken.None);

                Assert.Equal("client-b", loaded.ClientId);
                Assert.Equal("Клиент Б", loaded.ClientName);
            }
            finally
            {
                Delete(path);
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task SavedFile_IsDpapiProtectedAndModelHasNoSecretFields()
        {
            string path = TemporaryPath();
            try
            {
                var store = new LastAuthorizedClientHintStore(path);
                await store.SaveAsync(Hint("device-secret", "client-secret", "Секретный клиент"), CancellationToken.None);
                byte[] protectedBytes = File.ReadAllBytes(path);

                Assert.False(Contains(protectedBytes, Encoding.UTF8.GetBytes("device-secret")));
                Assert.False(Contains(protectedBytes, Encoding.UTF8.GetBytes("client-secret")));
                Assert.False(Contains(protectedBytes, Encoding.UTF8.GetBytes("Секретный клиент")));
                Assert.Equal(
                    ["ClientId", "ClientName", "DeviceId", "SavedAtUtc"],
                    typeof(LastAuthorizedClientHint).GetProperties().Select(property => property.Name).OrderBy(name => name));
            }
            finally
            {
                Delete(path);
            }
        }

        [Theory]
        [InlineData(LicenseDecision.DeviceNotRegistered)]
        [InlineData(LicenseDecision.ClientDisabled)]
        [InlineData(LicenseDecision.LicenseNotIssued)]
        public void CreateForAllowed_DoesNotCreateHintForRestrictedOrUnlicensedState(
            LicenseDecision decision)
        {
            LastAuthorizedClientHint hint = LastAuthorizedClientHint.CreateForAllowed(
                Client("client-a", "Клиент А"),
                Snapshot(decision, "client-a", "device-a"),
                "device-a");

            Assert.Null(hint);
        }

        [Fact]
        public void CreateForAllowed_DoesNotCreateHintForAnotherDeviceOrClient()
        {
            Assert.Null(LastAuthorizedClientHint.CreateForAllowed(
                Client("client-a", "Клиент А"), Snapshot(LicenseDecision.Allowed, "client-a", "device-b"), "device-a"));
            Assert.Null(LastAuthorizedClientHint.CreateForAllowed(
                Client("client-a", "Клиент А"), Snapshot(LicenseDecision.Allowed, "client-b", "device-a"), "device-a"));
        }

        private static LastAuthorizedClientHint Hint(string deviceId, string clientId, string clientName) => new()
        {
            DeviceId = deviceId,
            ClientId = clientId,
            ClientName = clientName
        };

        private static HonestFlow.Models.IPData Client(string clientId, string name) => new()
        {
            ClientId = clientId,
            Name = name
        };

        private static LicenseObservationSnapshot Snapshot(
            LicenseDecision decision,
            string clientId,
            string deviceId) => new()
        {
            Decision = decision,
            ClientId = clientId,
            DeviceId = deviceId
        };

        private static string TemporaryPath() => Path.Combine(
            Path.GetTempPath(), "HonestFlow.Tests", Guid.NewGuid().ToString("N") + ".dpapi");

        private static void Delete(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        private static bool Contains(byte[] source, byte[] value)
        {
            for (int i = 0; i <= source.Length - value.Length; i++)
            {
                int index = 0;
                while (index < value.Length && source[i + index] == value[index])
                    index++;
                if (index == value.Length)
                    return true;
            }

            return false;
        }
    }
}
