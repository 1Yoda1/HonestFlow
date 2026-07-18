using System;
using System.Collections.Generic;
using System.Linq;
using HonestFlow.Models.Licensing;
using Newtonsoft.Json;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class LicenseManifestTests
    {
        [Fact]
        public void SerializeAndDeserialize_PreservesTypedManifest()
        {
            var manifest = CreateValidManifest();

            string json = JsonConvert.SerializeObject(manifest);
            LicenseManifest restored = JsonConvert.DeserializeObject<LicenseManifest>(json);

            Assert.Contains("\"Diagnostics\"", json);
            Assert.NotNull(restored);
            Assert.Equal(3, restored.SchemaVersion);
            Assert.Equal(42, restored.Revision);
            Assert.Equal(TimeSpan.Zero, restored.IssuedAtUtc.Offset);
            Assert.Equal(TimeSpan.Zero, restored.ValidUntilUtc.Offset);
            Assert.Equal("client-1", restored.Clients.Single().ClientId);
            Assert.Contains(LicenseFeature.Diagnostics, restored.Clients.Single().Features);
            Assert.Equal("device-1", restored.Clients.Single().Devices.Single().DeviceId);
        }

        [Fact]
        public void UtcProperties_NormalizeNonUtcOffsets()
        {
            var source = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.FromHours(6));
            var manifest = new LicenseManifest
            {
                IssuedAtUtc = source,
                ValidUntilUtc = source.AddHours(1)
            };

            Assert.Equal(TimeSpan.Zero, manifest.IssuedAtUtc.Offset);
            Assert.Equal(source.UtcDateTime, manifest.IssuedAtUtc.UtcDateTime);
        }

        [Fact]
        public void Validate_AcceptsValidManifest()
        {
            Assert.Empty(LicenseManifestValidator.Validate(CreateValidManifest()));
        }

        [Fact]
        public void Validate_ReportsAllRequiredRules()
        {
            var manifest = CreateValidManifest();
            manifest.Revision = -1;
            manifest.ValidUntilUtc = manifest.IssuedAtUtc.AddMinutes(-1);
            manifest.Clients = new List<ClientLicense>
            {
                new ClientLicense
                {
                    ClientId = " duplicate ",
                    OfflineGraceHours = -1,
                    Devices = new List<LicensedDevice>
                    {
                        new LicensedDevice { DeviceId = "DEVICE" },
                        new LicensedDevice { DeviceId = " device " }
                    }
                },
                new ClientLicense { ClientId = "DUPLICATE" },
                new ClientLicense { ClientId = " " }
            };

            IReadOnlyList<LicenseValidationError> errors = LicenseManifestValidator.Validate(manifest);

            Assert.Contains(errors, error => error.Path == "Revision");
            Assert.Contains(errors, error => error.Path == "ValidUntilUtc");
            Assert.Contains(errors, error => error.Path == "Clients[0].OfflineGraceHours");
            Assert.Equal(2, errors.Count(error => error.Message == "ClientId must be unique."));
            Assert.Contains(errors, error => error.Path == "Clients[2].ClientId" && error.Message == "ClientId is required.");
            Assert.Equal(2, errors.Count(error => error.Message == "DeviceId must be unique within the client."));
        }

        private static LicenseManifest CreateValidManifest()
        {
            return new LicenseManifest
            {
                SchemaVersion = 3,
                Revision = 42,
                IssuedAtUtc = new DateTimeOffset(2026, 7, 18, 6, 0, 0, TimeSpan.Zero),
                ValidUntilUtc = new DateTimeOffset(2027, 7, 18, 6, 0, 0, TimeSpan.Zero),
                Clients = new List<ClientLicense>
                {
                    new ClientLicense
                    {
                        ClientId = "client-1",
                        Enabled = true,
                        MinHonestFlowVersion = "2.4.2.0",
                        OfflineGraceHours = 72,
                        Features = new List<LicenseFeature>
                        {
                            LicenseFeature.Diagnostics,
                            LicenseFeature.SendLogs,
                            LicenseFeature.Install,
                            LicenseFeature.Repair,
                            LicenseFeature.AutoFix,
                            LicenseFeature.ManualTools
                        },
                        Devices = new List<LicensedDevice>
                        {
                            new LicensedDevice
                            {
                                DeviceId = "device-1",
                                Name = "Cash desk 1",
                                Enabled = true,
                                Comment = "Primary device"
                            }
                        }
                    }
                }
            };
        }
    }
}
