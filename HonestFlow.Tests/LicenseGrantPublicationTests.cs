using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using HonestFlow.LicenseSigning;
using HonestFlow.Models.Licensing;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class LicenseGrantPublicationTests
    {
        [Fact]
        public void BuildClientGrants_DoesNotLeakOtherClientsOrDevices()
        {
            using var key = new TestEcdsaKey("test");
            var manifest = new LicenseManifest
            {
                SchemaVersion = 1,
                Revision = 12,
                IssuedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                ValidUntilUtc = DateTimeOffset.UtcNow.AddDays(2),
                Clients = new List<ClientLicense>
                {
                    Client("client-a", "device-a", "Адрес A"),
                    Client("client-b", "device-b", "Секретный адрес B")
                }
            };

            IReadOnlyList<LicenseGrantPublication> publications =
                new LicenseGrantPublicationBuilder(key).BuildClientGrants(
                    manifest, "test", string.Empty,
                    new Dictionary<string, bool?> { ["client-a"] = true, ["client-b"] = true });

            Assert.Equal(2, publications.Count);
            string firstJson = Encoding.UTF8.GetString(publications[0].GrantBytes);
            Assert.Contains("client-a", firstJson);
            Assert.Contains("device-a", firstJson);
            Assert.DoesNotContain("client-b", firstJson);
            Assert.DoesNotContain("device-b", firstJson);
            Assert.DoesNotContain("Секретный адрес B", firstJson);
        }

        [Fact]
        public void BuildOperatorGrants_CreatesSubjectBoundOperatorGrant()
        {
            using var key = new TestEcdsaKey("test");
            var manifest = new LicenseManifest
            {
                SchemaVersion = 1,
                Revision = 13,
                IssuedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                ValidUntilUtc = DateTimeOffset.UtcNow.AddDays(2),
                Clients = new List<ClientLicense> { Client("client-a", "device-a", null) },
                OperatorDevices = new List<OperatorDevice>
                {
                    new() { DeviceId = "operator-device", Enabled = true }
                }
            };

            LicenseGrantPublication publication = Assert.Single(
                new LicenseGrantPublicationBuilder(key).BuildOperatorGrants(
                    manifest, "test", string.Empty,
                    new Dictionary<string, bool?> { ["client-a"] = true }));
            string json = Encoding.UTF8.GetString(publication.GrantBytes);
            Assert.Contains("operator-device", json);
            Assert.Contains("\"OperatorDevice\": true", json);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        [InlineData(null, true)]
        public void BuildClientGrants_DerivesClientEnabledFromPolicy(
            bool? policyEnabled,
            bool expectedClientEnabled)
        {
            using var key = new TestEcdsaKey("test");
            ClientLicense client = Client("client-a", "device-a", null);
            client.Enabled = !expectedClientEnabled;
            var manifest = new LicenseManifest
            {
                SchemaVersion = 1,
                Revision = 14,
                IssuedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                ValidUntilUtc = DateTimeOffset.UtcNow.AddDays(2),
                Clients = new List<ClientLicense> { client }
            };

            LicenseGrantPublication publication = Assert.Single(
                new LicenseGrantPublicationBuilder(key).BuildClientGrants(
                    manifest, "test", string.Empty,
                    new Dictionary<string, bool?> { ["client-a"] = policyEnabled }));
            string json = Encoding.UTF8.GetString(publication.GrantBytes);

            Assert.Contains($"\"ClientEnabled\": {expectedClientEnabled.ToString().ToLowerInvariant()}", json);
        }

        private static ClientLicense Client(string clientId, string deviceId, string address) => new()
        {
            ClientId = clientId,
            Enabled = true,
            MinHonestFlowVersion = "3.0.0",
            OfflineGraceHours = 24,
            Features = new List<LicenseFeature> { LicenseFeature.ViewAndRepair },
            Devices = new List<LicensedDevice>
            {
                new() { DeviceId = deviceId, Enabled = true, Address = address }
            }
        };
    }
}
