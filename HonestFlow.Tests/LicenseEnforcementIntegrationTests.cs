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
        public void RuntimeConfiguration_DefaultIsEnforced()
        {
            Assert.Equal(
                LicenseEnforcementMode.Enforced,
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
        public void Enforced_BaseSupportAccessDoesNotRequireManifestFlags()
        {
            var store = new LicenseObservationSnapshotStore();
            store.Set(Snapshot(
                LicenseDecision.Allowed,
                LicenseFeature.Diagnostics,
                LicenseFeature.Install));
            var policy = Enforced(store);

            Assert.True(policy.Check(LicenseFeature.Diagnostics).IsAllowed);
            Assert.True(policy.Check(LicenseFeature.Install).IsAllowed);
            Assert.True(policy.Check(LicenseFeature.SendLogs).IsAllowed);
            Assert.True(policy.Check(LicenseFeature.CollectDiagnostics).IsAllowed);
            Assert.True(policy.Check(LicenseFeature.SendDiagnostics).IsAllowed);
            Assert.True(policy.Check(LicenseFeature.RequestHelp).IsAllowed);
            Assert.True(policy.Check(LicenseFeature.InstallRuDesktop).IsAllowed);
            Assert.True(policy.Check(LicenseFeature.ConfigureRuDesktop).IsAllowed);
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
        public void Enforced_VersionTooOldKeepsBaseSupportAccess()
        {
            var store = new LicenseObservationSnapshotStore();
            store.Set(Snapshot(LicenseDecision.VersionTooOld, LicenseFeature.SendLogs, LicenseFeature.Install));
            var policy = Enforced(store);

            Assert.True(policy.Check(LicenseFeature.Diagnostics).IsAllowed);
            Assert.True(policy.Check(LicenseFeature.SendLogs).IsAllowed);
            Assert.True(policy.Check(LicenseFeature.RequestHelp).IsAllowed);
            Assert.True(policy.Check(LicenseFeature.InstallRuDesktop).IsAllowed);
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

        [Fact]
        public void Enforced_GranularFeatureDoesNotUnlockSiblingOperation()
        {
            var store = new LicenseObservationSnapshotStore();
            store.Set(Snapshot(
                LicenseDecision.Allowed,
                LicenseFeature.ReinstallComponents));
            var policy = Enforced(store);

            Assert.True(policy.Check(LicenseFeature.ReinstallComponents).IsAllowed);
            Assert.False(policy.Check(LicenseFeature.RestoreLmDatabase).IsAllowed);
            Assert.False(policy.Check(LicenseFeature.Repair).IsAllowed);
        }

        [Theory]
        [InlineData(LicenseFeature.Diagnostics, LicenseFeature.ViewPointStatus)]
        [InlineData(LicenseFeature.Diagnostics, LicenseFeature.CollectDiagnostics)]
        [InlineData(LicenseFeature.SendLogs, LicenseFeature.SendDiagnostics)]
        [InlineData(LicenseFeature.SendLogs, LicenseFeature.RequestHelp)]
        [InlineData(LicenseFeature.Install, LicenseFeature.InstallComponents)]
        [InlineData(LicenseFeature.Repair, LicenseFeature.ReinstallComponents)]
        [InlineData(LicenseFeature.Repair, LicenseFeature.RestoreLmDatabase)]
        [InlineData(LicenseFeature.AutoFix, LicenseFeature.ManageServices)]
        [InlineData(LicenseFeature.AutoFix, LicenseFeature.RecoverLmServices)]
        [InlineData(LicenseFeature.AutoFix, LicenseFeature.InitializeLm)]
        [InlineData(LicenseFeature.Install, LicenseFeature.InstallRuDesktop)]
        [InlineData(LicenseFeature.ManualTools, LicenseFeature.ConfigureRuDesktop)]
        [InlineData(LicenseFeature.ManualTools, LicenseFeature.OpenLocalTools)]
        public void Enforced_LegacyFeatureGrantsMappedGranularFeature(
            LicenseFeature legacyFeature,
            LicenseFeature granularFeature)
        {
            var store = new LicenseObservationSnapshotStore();
            store.Set(Snapshot(LicenseDecision.Allowed, legacyFeature));
            var policy = Enforced(store);

            Assert.True(policy.Check(granularFeature).IsAllowed);
        }

        [Fact]
        public void Enforced_RuDesktopAccessIsAlwaysAvailable()
        {
            var installStore = new LicenseObservationSnapshotStore();
            installStore.Set(Snapshot(LicenseDecision.Allowed, LicenseFeature.Install));
            var installPolicy = Enforced(installStore);

            Assert.True(installPolicy.Check(LicenseFeature.InstallRuDesktop).IsAllowed);
            Assert.True(installPolicy.Check(LicenseFeature.ConfigureRuDesktop).IsAllowed);

            var toolsStore = new LicenseObservationSnapshotStore();
            toolsStore.Set(Snapshot(LicenseDecision.Allowed, LicenseFeature.ManualTools));
            var toolsPolicy = Enforced(toolsStore);

            Assert.True(toolsPolicy.Check(LicenseFeature.ConfigureRuDesktop).IsAllowed);
            Assert.True(toolsPolicy.Check(LicenseFeature.InstallRuDesktop).IsAllowed);
        }

        [Fact]
        public void FeatureCatalog_ExposesOnlyTwoLicenseLevels()
        {
            Assert.Equal(
                new[]
                {
                    LicenseFeature.ViewAndRepair,
                    LicenseFeature.InstallAndMaintenance
                },
                LicenseFeatureCatalog.ConfigurableFeatures);
            Assert.Equal(
                "Просмотр состояния и ремонт точки",
                LicenseFeatureCatalog.GetDisplayName(LicenseFeature.ViewAndRepair));
        }

        [Theory]
        [InlineData(LicenseFeature.ViewPointStatus)]
        [InlineData(LicenseFeature.ManageServices)]
        [InlineData(LicenseFeature.RecoverLmServices)]
        [InlineData(LicenseFeature.InitializeLm)]
        [InlineData(LicenseFeature.OpenLocalTools)]
        public void Enforced_ViewAndRepairGrantsPointWork(LicenseFeature operation)
        {
            var store = new LicenseObservationSnapshotStore();
            store.Set(Snapshot(LicenseDecision.Allowed, LicenseFeature.ViewAndRepair));
            var policy = Enforced(store);

            Assert.True(policy.Check(operation).IsAllowed);
            Assert.False(policy.Check(LicenseFeature.InstallComponents).IsAllowed);
            Assert.False(policy.Check(LicenseFeature.ReinstallComponents).IsAllowed);
        }

        [Theory]
        [InlineData(LicenseFeature.InstallComponents)]
        [InlineData(LicenseFeature.ReinstallComponents)]
        [InlineData(LicenseFeature.RestoreLmDatabase)]
        public void Enforced_InstallAndMaintenanceGrantsInstallationWork(LicenseFeature operation)
        {
            var store = new LicenseObservationSnapshotStore();
            store.Set(Snapshot(LicenseDecision.Allowed, LicenseFeature.InstallAndMaintenance));
            var policy = Enforced(store);

            Assert.True(policy.Check(operation).IsAllowed);
            Assert.False(policy.Check(LicenseFeature.ViewPointStatus).IsAllowed);
            Assert.False(policy.Check(LicenseFeature.ManageServices).IsAllowed);
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
