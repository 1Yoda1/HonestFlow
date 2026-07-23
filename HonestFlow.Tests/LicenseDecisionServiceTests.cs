using System;
using System.Collections.Generic;
using System.Linq;
using HonestFlow.Application.Licensing;
using HonestFlow.Models.Licensing;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class LicenseDecisionServiceTests
    {
        private static readonly DateTimeOffset NowUtc =
            new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Decide_ReturnsAllowedWithLicensedFeatures()
        {
            LicenseDecisionContext context = CreateValidContext();

            LicenseDecisionResult result = CreateService().Decide(context);

            Assert.Equal(LicenseDecision.Allowed, result.Decision);
            Assert.Equal("LICENSE_ALLOWED", result.TechnicalCode);
            Assert.Contains(LicenseFeature.Install, result.Features);
            Assert.Equal(new Version(2, 4, 2, 0), result.MinimumRequiredVersion);
        }

        [Fact]
        public void Decide_ReturnsClientNotFoundForNonExactClientId()
        {
            LicenseDecisionContext context = CreateValidContext();
            context.ClientId = "CLIENT-1";

            LicenseDecisionResult result = CreateService().Decide(context);

            Assert.Equal(LicenseDecision.ClientNotFound, result.Decision);
            Assert.Empty(result.Features);
        }

        [Fact]
        public void Decide_OperatorDeviceAllowsAllFeaturesForAnyClient()
        {
            LicenseDecisionContext context = CreateValidContext();
            context.ClientId = "another-client";
            context.CurrentHonestFlowVersion = new Version(1, 0);
            context.Manifest.OperatorDevices = new List<OperatorDevice>
            {
                new OperatorDevice
                {
                    DeviceId = context.DeviceId.ToUpperInvariant(),
                    Name = "Owner workstation",
                    Enabled = true
                }
            };

            LicenseDecisionResult result = CreateService().Decide(context);

            Assert.Equal(LicenseDecision.Allowed, result.Decision);
            Assert.Equal("LICENSE_OPERATOR_DEVICE_ALLOWED", result.TechnicalCode);
            Assert.Equal(
                Enum.GetValues(typeof(LicenseFeature)).Length,
                result.Features.Distinct().Count());
        }

        [Fact]
        public void Decide_DisabledOperatorDeviceUsesNormalClientRules()
        {
            LicenseDecisionContext context = CreateValidContext();
            context.ClientId = "another-client";
            context.Manifest.OperatorDevices = new List<OperatorDevice>
            {
                new OperatorDevice { DeviceId = context.DeviceId, Enabled = false }
            };

            LicenseDecisionResult result = CreateService().Decide(context);

            Assert.Equal(LicenseDecision.ClientNotFound, result.Decision);
        }

        [Fact]
        public void Decide_OperatorDeviceDoesNotBypassManifestExpiration()
        {
            LicenseDecisionContext context = CreateValidContext();
            context.Manifest.ValidUntilUtc = NowUtc.AddMinutes(-1);
            context.Manifest.OperatorDevices = new List<OperatorDevice>
            {
                new OperatorDevice { DeviceId = context.DeviceId, Enabled = true }
            };

            LicenseDecisionResult result = CreateService().Decide(context);

            Assert.Equal(LicenseDecision.ManifestExpired, result.Decision);
        }

        [Fact]
        public void Decide_ReturnsClientDisabled()
        {
            LicenseDecisionContext context = CreateValidContext();
            context.Manifest.Clients[0].Enabled = false;

            LicenseDecisionResult result = CreateService().Decide(context);

            Assert.Equal(LicenseDecision.ClientDisabled, result.Decision);
        }

        [Fact]
        public void Decide_ReturnsDeviceNotRegisteredForNonExactDeviceId()
        {
            LicenseDecisionContext context = CreateValidContext();
            context.DeviceId = "DEVICE-1";

            LicenseDecisionResult result = CreateService().Decide(context);

            Assert.Equal(LicenseDecision.DeviceNotRegistered, result.Decision);
        }

        [Fact]
        public void Decide_ReturnsDeviceDisabled()
        {
            LicenseDecisionContext context = CreateValidContext();
            context.Manifest.Clients[0].Devices[0].Enabled = false;

            LicenseDecisionResult result = CreateService().Decide(context);

            Assert.Equal(LicenseDecision.DeviceDisabled, result.Decision);
        }

        [Fact]
        public void Decide_ReturnsVersionTooOldAndMinimumVersion()
        {
            LicenseDecisionContext context = CreateValidContext();
            context.CurrentHonestFlowVersion = new Version(2, 4, 1, 9);

            LicenseDecisionResult result = CreateService().Decide(context);

            Assert.Equal(LicenseDecision.VersionTooOld, result.Decision);
            Assert.Equal(new Version(2, 4, 2, 0), result.MinimumRequiredVersion);
        }

        [Fact]
        public void Decide_ReturnsManifestExpiredForRemoteManifest()
        {
            LicenseDecisionContext context = CreateValidContext();
            context.Manifest.ValidUntilUtc = NowUtc.AddTicks(-1);

            LicenseDecisionResult result = CreateService().Decide(context);

            Assert.Equal(LicenseDecision.ManifestExpired, result.Decision);
        }

        [Fact]
        public void Decide_ReturnsOfflineGraceExpiredAndGraceEnd()
        {
            LicenseDecisionContext context = CreateValidContext();
            context.ManifestSource = LicenseManifestSource.Cache;
            context.LastSuccessfulOnlineCheckUtc = NowUtc.AddHours(-25);
            context.Manifest.Clients[0].OfflineGraceHours = 24;

            LicenseDecisionResult result = CreateService().Decide(context);

            Assert.Equal(LicenseDecision.OfflineGraceExpired, result.Decision);
            Assert.Equal(NowUtc.AddHours(-1), result.OfflineGraceEndsAtUtc);
        }

        [Fact]
        public void Decide_ReturnsInvalidLicenseStateWhenCacheOnlineTimeIsMissing()
        {
            LicenseDecisionContext context = CreateValidContext();
            context.ManifestSource = LicenseManifestSource.Cache;
            context.LastSuccessfulOnlineCheckUtc = null;

            LicenseDecisionResult result = CreateService().Decide(context);

            Assert.Equal(LicenseDecision.InvalidLicenseState, result.Decision);
            Assert.Equal("LICENSE_ONLINE_CHECK_TIME_MISSING", result.TechnicalCode);
        }

        [Fact]
        public void Decide_ReturnsInvalidLicenseStateForMalformedManifest()
        {
            LicenseDecisionContext context = CreateValidContext();
            context.Manifest.Revision = -1;

            LicenseDecisionResult result = CreateService().Decide(context);

            Assert.Equal(LicenseDecision.InvalidLicenseState, result.Decision);
            Assert.Equal("LICENSE_MANIFEST_INVALID", result.TechnicalCode);
        }

        [Fact]
        public void Decide_AllowsDiagnosticsAndSendLogsOnDenialOnlyWhenPolicyExplicitlyAllowsThem()
        {
            LicenseDecisionContext context = CreateValidContext();
            context.Manifest.Clients[0].Enabled = false;
            var policy = new LicenseDecisionPolicy
            {
                AllowDiagnosticsWhenDenied = true,
                AllowSendLogsWhenDenied = true
            };

            LicenseDecisionResult result = CreateService(policy).Decide(context);

            Assert.Equal(
                new[] { LicenseFeature.Diagnostics, LicenseFeature.SendLogs },
                result.Features.OrderBy(feature => feature));
            Assert.DoesNotContain(LicenseFeature.Install, result.Features);
        }

        [Fact]
        public void Decide_DoesNotApplyLegacyFallbackForMissingClient()
        {
            LicenseDecisionContext context = CreateValidContext();
            context.ClientId = "legacy-client-without-license";
            var policy = new LicenseDecisionPolicy
            {
                AllowDiagnosticsWhenDenied = true,
                AllowSendLogsWhenDenied = true
            };

            LicenseDecisionResult result = CreateService(policy).Decide(context);

            Assert.Equal(LicenseDecision.ClientNotFound, result.Decision);
            Assert.False(result.IsAllowed);
            Assert.Empty(result.Features);
        }

        private static LicenseDecisionService CreateService(LicenseDecisionPolicy policy = null)
        {
            return new LicenseDecisionService(policy, () => NowUtc);
        }

        [Fact]
        public void Decide_ReturnsAddressOfMatchedDevice()
        {
            LicenseDecisionContext context = CreateValidContext();
            context.Manifest.Clients[0].Devices[0].Address = "ул. Ленина, 10";

            LicenseDecisionResult result = CreateService().Decide(context);

            Assert.Equal(LicenseDecision.Allowed, result.Decision);
            Assert.Equal("ул. Ленина, 10", result.PointAddress);
        }

        private static LicenseDecisionContext CreateValidContext()
        {
            return new LicenseDecisionContext
            {
                ClientId = "client-1",
                DeviceId = "device-1",
                CurrentHonestFlowVersion = new Version(2, 4, 2, 0),
                ManifestSource = LicenseManifestSource.Remote,
                Manifest = new LicenseManifest
                {
                    SchemaVersion = 1,
                    Revision = 10,
                    IssuedAtUtc = NowUtc.AddDays(-1),
                    ValidUntilUtc = NowUtc.AddDays(30),
                    Clients = new List<ClientLicense>
                    {
                        new ClientLicense
                        {
                            ClientId = "client-1",
                            Enabled = true,
                            MinHonestFlowVersion = "2.4.2.0",
                            OfflineGraceHours = 24,
                            Features = new List<LicenseFeature>
                            {
                                LicenseFeature.Diagnostics,
                                LicenseFeature.SendLogs,
                                LicenseFeature.Install
                            },
                            Devices = new List<LicensedDevice>
                            {
                                new LicensedDevice
                                {
                                    DeviceId = "device-1",
                                    Name = "Test device",
                                    Enabled = true
                                }
                            }
                        }
                    }
                }
            };
        }
    }
}
