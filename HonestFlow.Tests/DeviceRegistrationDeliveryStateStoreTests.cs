using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Licensing;
using Newtonsoft.Json;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class DeviceRegistrationDeliveryStateStoreTests : IDisposable
    {
        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes("HonestFlow.DeviceRegistrationDelivery.v1");
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "honestflow-registration-state-tests-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task NewAddressRequest_IsSentOnceEvenWhenLegacyMarkerExists()
        {
            Directory.CreateDirectory(_folder);
            string path = Path.Combine(_folder, "state.dpapi");
            WriteLegacyState(path, "client-1", "device-1");
            var store = new DpapiDeviceRegistrationDeliveryStateStore(path);

            Assert.False(await store.WasSentAsync(
                "client-1", "device-1", CancellationToken.None));

            await store.MarkSentAsync("client-1", "device-1", CancellationToken.None);

            Assert.True(await store.WasSentAsync(
                "client-1", "device-1", CancellationToken.None));
        }

        public void Dispose()
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, true);
        }

        private static void WriteLegacyState(string path, string clientId, string deviceId)
        {
            using SHA256 sha256 = SHA256.Create();
            string legacyKey = Convert.ToBase64String(sha256.ComputeHash(
                Encoding.UTF8.GetBytes(clientId + "\n" + deviceId)));
            byte[] json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(
                new HashSet<string>(StringComparer.Ordinal) { legacyKey }));
            File.WriteAllBytes(
                path,
                ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser));
        }
    }
}
