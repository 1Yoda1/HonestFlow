using System;
using System.Collections.Generic;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Licensing;
using HonestFlow.Models.Licensing;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class LegacyLicenseGrantExtractorTests
    {
        [Fact]
        public void Extract_ReturnsOnlyRequestedDevice()
        {
            var manifest = new LicenseManifest
            {
                SchemaVersion = 1,
                Revision = 9,
                IssuedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                ValidUntilUtc = DateTimeOffset.UtcNow.AddDays(2),
                Clients = new List<ClientLicense>
                {
                    new()
                    {
                        ClientId = "client-a",
                        Enabled = true,
                        MinHonestFlowVersion = "3.0.0",
                        OfflineGraceHours = 24,
                        Features = new List<LicenseFeature> { LicenseFeature.ViewAndRepair },
                        Devices = new List<LicensedDevice>
                        {
                            new() { DeviceId = "device-a", Enabled = true, Address = "Address A" },
                            new() { DeviceId = "device-b", Enabled = true, Address = "Address B" }
                        }
                    }
                }
            };

            LicenseGrant grant = LegacyLicenseGrantExtractor.Extract(
                manifest,
                new LicenseGrantRequest("client-a", "device-a"));

            Assert.NotNull(grant);
            Assert.Equal("device-a", grant.DeviceId);
            Assert.Equal("Address A", grant.PointAddress);
            Assert.DoesNotContain("device-b", Newtonsoft.Json.JsonConvert.SerializeObject(grant));
        }

        [Fact]
        public void Extract_ReturnsNullForUnknownDevice()
        {
            var manifest = new LicenseManifest
            {
                SchemaVersion = 1,
                Revision = 1,
                IssuedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                ValidUntilUtc = DateTimeOffset.UtcNow.AddHours(1),
                Clients = new List<ClientLicense>
                {
                    new()
                    {
                        ClientId = "client-a",
                        Enabled = true,
                        MinHonestFlowVersion = "3.0.0",
                        Devices = new List<LicensedDevice>()
                    }
                }
            };

            Assert.Null(LegacyLicenseGrantExtractor.Extract(
                manifest,
                new LicenseGrantRequest("client-a", "missing")));
        }
    }
}
