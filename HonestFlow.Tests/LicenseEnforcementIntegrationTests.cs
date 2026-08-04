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
        public void RuntimeConfiguration_UserEnvironmentCannotDisableEnforcementOrReplaceTrust()
        {
            const string modeName = "HONESTFLOW_LICENSE_ENFORCEMENT_MODE";
            const string manifestName = "HONESTFLOW_LICENSE_MANIFEST_URL";
            const string signatureName = "HONESTFLOW_LICENSE_SIGNATURE_URL";
            const string keyIdName = "HONESTFLOW_LICENSE_KEY_ID";
            const string publicKeyName = "HONESTFLOW_LICENSE_PUBLIC_KEY";
            string oldMode = Environment.GetEnvironmentVariable(modeName);
            string oldManifest = Environment.GetEnvironmentVariable(manifestName);
            string oldSignature = Environment.GetEnvironmentVariable(signatureName);
            string oldKeyId = Environment.GetEnvironmentVariable(keyIdName);
            string oldPublicKey = Environment.GetEnvironmentVariable(publicKeyName);

            try
            {
                Environment.SetEnvironmentVariable(modeName, "Disabled");
                Environment.SetEnvironmentVariable(manifestName, "https://attacker.invalid/licenses.json");
                Environment.SetEnvironmentVariable(signatureName, "https://attacker.invalid/licenses.json.sig");
                Environment.SetEnvironmentVariable(keyIdName, "attacker-key");
                Environment.SetEnvironmentVariable(publicKeyName, "attacker-public-key");

                LicenseRuntimeConfiguration configuration = LicenseRuntimeConfiguration.FromEnvironment();

                Assert.Equal(LicenseEnforcementMode.Enforced, configuration.EnforcementMode);
                Assert.Null(configuration.ManifestUrl);
                Assert.Null(configuration.SignatureUrl);
                Assert.Null(configuration.KeyId);
                Assert.Null(configuration.PublicKeySubjectPublicKeyInfoBase64);
            }
            finally
            {
                Environment.SetEnvironmentVariable(modeName, oldMode);
                Environment.SetEnvironmentVariable(manifestName, oldManifest);
                Environment.SetEnvironmentVariable(signatureName, oldSignature);
                Environment.SetEnvironmentVariable(keyIdName, oldKeyId);
                Environment.SetEnvironmentVariable(publicKeyName, oldPublicKey);
            }
        }

        [Fact]
        public void ManifestFeatureEnum_ContainsExactlyTwoTags()
        {
            Assert.Equal(
                new[]
                {
                    LicenseFeature.ViewAndRepair,
                    LicenseFeature.InstallAndMaintenance
                },
                Enum.GetValues<LicenseFeature>());
        }

        [Fact]
        public void ObserveOnly_DoesNotRestrictOperations()
        {
            var store = new LicenseObservationSnapshotStore();
            store.Set(Snapshot(LicenseDecision.ClientDisabled));
            var policy = new LicenseAccessPolicy(LicenseEnforcementMode.ObserveOnly, store);

            Assert.True(policy.Check(LicenseOperation.InstallComponents).IsAllowed);
            Assert.True(policy.Check(LicenseOperation.OpenLocalTools).IsAllowed);
        }

        [Theory]
        [InlineData(LicenseOperation.CollectDiagnostics)]
        [InlineData(LicenseOperation.SendDiagnostics)]
        [InlineData(LicenseOperation.RequestHelp)]
        [InlineData(LicenseOperation.InstallRuDesktop)]
        [InlineData(LicenseOperation.ConfigureRuDesktop)]
        public void Enforced_BaseSupportOperationsDoNotRequireTags(LicenseOperation operation)
        {
            var store = new LicenseObservationSnapshotStore();
            store.Set(Snapshot(LicenseDecision.Allowed));

            Assert.True(Enforced(store).Check(operation).IsAllowed);
        }

        [Theory]
        [InlineData(LicenseDecision.ClientDisabled)]
        [InlineData(LicenseDecision.DeviceNotRegistered)]
        [InlineData(LicenseDecision.DeviceDisabled)]
        [InlineData(LicenseDecision.OfflineGraceExpired)]
        [InlineData(LicenseDecision.InvalidLicenseState)]
        public void Enforced_DenialBlocksLicensedOperations(LicenseDecision decision)
        {
            var store = new LicenseObservationSnapshotStore();
            store.Set(Snapshot(
                decision,
                LicenseFeature.ViewAndRepair,
                LicenseFeature.InstallAndMaintenance));
            var policy = Enforced(store);

            Assert.True(policy.Check(LicenseOperation.CollectDiagnostics).IsAllowed);
            Assert.False(policy.Check(LicenseOperation.ViewPointStatus).IsAllowed);
            Assert.False(policy.Check(LicenseOperation.InstallComponents).IsAllowed);
        }

        [Fact]
        public void Enforced_WithoutDecisionFailsSafeToBaseSupport()
        {
            var policy = Enforced(new LicenseObservationSnapshotStore());

            Assert.True(policy.Check(LicenseOperation.CollectDiagnostics).IsAllowed);
            Assert.False(policy.Check(LicenseOperation.InstallComponents).IsAllowed);
            Assert.Equal(
                "LICENSE_DECISION_PENDING",
                policy.Check(LicenseOperation.InstallComponents).TechnicalCode);
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
            Assert.False(policy.Check(LicenseOperation.InstallComponents).IsAllowed);
            Assert.True(policy.Check(LicenseOperation.CollectDiagnostics).IsAllowed);
        }

        [Theory]
        [InlineData(LicenseOperation.ViewPointStatus)]
        [InlineData(LicenseOperation.ManageServices)]
        [InlineData(LicenseOperation.RecoverLmServices)]
        [InlineData(LicenseOperation.InitializeLm)]
        [InlineData(LicenseOperation.OpenLocalTools)]
        public void Enforced_ViewAndRepairGrantsOnlyPointWork(LicenseOperation operation)
        {
            var store = new LicenseObservationSnapshotStore();
            store.Set(Snapshot(LicenseDecision.Allowed, LicenseFeature.ViewAndRepair));
            var policy = Enforced(store);

            Assert.True(policy.Check(operation).IsAllowed);
            Assert.False(policy.Check(LicenseOperation.InstallComponents).IsAllowed);
            Assert.False(policy.Check(LicenseOperation.ReinstallComponents).IsAllowed);
        }

        [Theory]
        [InlineData(LicenseOperation.InstallComponents)]
        [InlineData(LicenseOperation.ReinstallComponents)]
        [InlineData(LicenseOperation.RestoreLmDatabase)]
        public void Enforced_InstallAndMaintenanceGrantsOnlyInstallationWork(
            LicenseOperation operation)
        {
            var store = new LicenseObservationSnapshotStore();
            store.Set(Snapshot(
                LicenseDecision.Allowed,
                LicenseFeature.InstallAndMaintenance));
            var policy = Enforced(store);

            Assert.True(policy.Check(operation).IsAllowed);
            Assert.False(policy.Check(LicenseOperation.ViewPointStatus).IsAllowed);
            Assert.False(policy.Check(LicenseOperation.ManageServices).IsAllowed);
        }

        [Fact]
        public void FeatureCatalog_ExposesOnlyTwoLicenseTags()
        {
            Assert.Equal(
                new[]
                {
                    LicenseFeature.ViewAndRepair,
                    LicenseFeature.InstallAndMaintenance
                },
                LicenseFeatureCatalog.ConfigurableFeatures);
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
                        LicenseFeature.ViewAndRepair,
                        LicenseFeature.InstallAndMaintenance
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
