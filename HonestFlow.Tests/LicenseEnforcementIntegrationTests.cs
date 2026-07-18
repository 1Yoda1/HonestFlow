using System;
using System.Collections.Generic;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Licensing;
using HonestFlow.Models.Licensing;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class LicenseEnforcementIntegrationTests
    {
        private static readonly DateTimeOffset NowUtc =
            new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void RuntimeConfiguration_DefaultRemainsObserveOnly()
        {
            Assert.Equal(
                LicenseEnforcementMode.ObserveOnly,
                new LicenseRuntimeConfiguration().EnforcementMode);
        }

        [Fact]
        public void ObserveOnly_DoesNotRestrictFeatures()
        {
            var store = new LicenseObservationSnapshotStore();
            store.Set(Snapshot(LicenseDecision.ClientDisabled));
            var policy = new LicenseAccessPolicy(LicenseEnforcementMode.ObserveOnly, store);

            Assert.True(policy.Check(LicenseFeature.Install).IsAllowed);
            Assert.True(policy.Check(LicenseFeature.ManualTools).IsAllowed);
        }

        [Fact]
        public void Enforced_AllowedUsesExactLicensedFeatures()
        {
            var store = new LicenseObservationSnapshotStore();
            store.Set(Snapshot(
                LicenseDecision.Allowed,
                LicenseFeature.Diagnostics,
                LicenseFeature.Install));
            var policy = Enforced(store);

            Assert.True(policy.Check(LicenseFeature.Diagnostics).IsAllowed);
            Assert.True(policy.Check(LicenseFeature.Install).IsAllowed);
            Assert.False(policy.Check(LicenseFeature.SendLogs).IsAllowed);
            Assert.False(policy.Check(LicenseFeature.AutoFix).IsAllowed);
        }

        [Theory]
        [InlineData(LicenseDecision.ClientDisabled)]
        [InlineData(LicenseDecision.DeviceNotRegistered)]
        [InlineData(LicenseDecision.DeviceDisabled)]
        [InlineData(LicenseDecision.OfflineGraceExpired)]
        [InlineData(LicenseDecision.InvalidLicenseState)]
        public void Enforced_DenialKeepsOnlyDiagnosticsAndSendLogs(LicenseDecision decision)
        {
            var store = new LicenseObservationSnapshotStore();
            store.Set(Snapshot(decision, LicenseFeature.Install, LicenseFeature.ManualTools));
            var policy = Enforced(store);

            Assert.True(policy.Check(LicenseFeature.Diagnostics).IsAllowed);
            Assert.True(policy.Check(LicenseFeature.SendLogs).IsAllowed);
            Assert.False(policy.Check(LicenseFeature.Install).IsAllowed);
            Assert.False(policy.Check(LicenseFeature.Repair).IsAllowed);
            Assert.False(policy.Check(LicenseFeature.AutoFix).IsAllowed);
            Assert.False(policy.Check(LicenseFeature.ManualTools).IsAllowed);
        }

        [Fact]
        public void Enforced_VersionTooOldKeepsDiagnosticsOnly()
        {
            var store = new LicenseObservationSnapshotStore();
            store.Set(Snapshot(LicenseDecision.VersionTooOld, LicenseFeature.SendLogs, LicenseFeature.Install));
            var policy = Enforced(store);

            Assert.True(policy.Check(LicenseFeature.Diagnostics).IsAllowed);
            Assert.False(policy.Check(LicenseFeature.SendLogs).IsAllowed);
            Assert.False(policy.Check(LicenseFeature.Install).IsAllowed);
        }

        [Fact]
        public void Enforced_WithoutDecisionFailsSafeToDiagnosticMode()
        {
            var policy = Enforced(new LicenseObservationSnapshotStore());

            Assert.True(policy.Check(LicenseFeature.Diagnostics).IsAllowed);
            Assert.True(policy.Check(LicenseFeature.SendLogs).IsAllowed);
            Assert.False(policy.Check(LicenseFeature.Install).IsAllowed);
            Assert.Equal("LICENSE_DECISION_PENDING", policy.Check(LicenseFeature.Install).TechnicalCode);
        }

        [Fact]
        public void DecisionServiceAndEnforcementPolicy_BlockUnregisteredDevice()
        {
            LicenseDecisionResult decision = new LicenseDecisionService(
                new LicenseDecisionPolicy(),
                () => NowUtc).Decide(new LicenseDecisionContext
                {
                    ClientId = "client-1",
                    DeviceId = "unregistered-device",
                    CurrentHonestFlowVersion = new Version(2, 4, 2, 0),
                    Manifest = Manifest(),
                    ManifestSource = LicenseManifestSource.Remote,
                    LastSuccessfulOnlineCheckUtc = NowUtc
                });

            var store = new LicenseObservationSnapshotStore();
            store.Set(new LicenseObservationSnapshot
            {
                Decision = decision.Decision,
                TechnicalCode = decision.TechnicalCode,
                Message = decision.Message,
                Features = decision.Features
            });

            LicenseAccessPolicy policy = Enforced(store);
            Assert.Equal(LicenseDecision.DeviceNotRegistered, decision.Decision);
            Assert.False(policy.Check(LicenseFeature.Install).IsAllowed);
            Assert.True(policy.Check(LicenseFeature.Diagnostics).IsAllowed);
        }

        private static LicenseAccessPolicy Enforced(ILicenseObservationSnapshotStore store) =>
            new(LicenseEnforcementMode.Enforced, store);

        private static LicenseObservationSnapshot Snapshot(
            LicenseDecision decision,
            params LicenseFeature[] features) => new()
            {
                Decision = decision,
                TechnicalCode = "TEST_" + decision.ToString().ToUpperInvariant(),
                Message = "Операция запрещена тестовой лицензией.",
                Features = features
            };

        private static LicenseManifest Manifest() => new()
        {
            SchemaVersion = 1,
            Revision = 1,
            IssuedAtUtc = NowUtc.AddDays(-1),
            ValidUntilUtc = NowUtc.AddDays(1),
            Clients = new List<ClientLicense>
            {
                new()
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
                        new() { DeviceId = "device-1", Enabled = true }
                    }
                }
            }
        };
    }
}
