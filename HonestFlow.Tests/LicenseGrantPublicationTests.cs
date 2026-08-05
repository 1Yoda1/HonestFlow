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
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string privatePem = "-----BEGIN PRIVATE KEY-----\n" +
                Convert.ToBase64String(key.ExportPkcs8PrivateKey(), Base64FormattingOptions.InsertLineBreaks) +
                "\n-----END PRIVATE KEY-----";
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
                new LicenseGrantPublicationBuilder().BuildClientGrants(manifest, "test", privatePem);

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
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string privatePem = "-----BEGIN PRIVATE KEY-----\n" +
                Convert.ToBase64String(key.ExportPkcs8PrivateKey(), Base64FormattingOptions.InsertLineBreaks) +
                "\n-----END PRIVATE KEY-----";
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
                new LicenseGrantPublicationBuilder().BuildOperatorGrants(manifest, "test", privatePem));
            string json = Encoding.UTF8.GetString(publication.GrantBytes);
            Assert.Contains("operator-device", json);
            Assert.Contains("\"OperatorDevice\": true", json);
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
