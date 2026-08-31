using System;
using System.Collections.Generic;
using HonestFlow.Application.Licensing;
using HonestFlow.Models.Licensing;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class LicenseDecisionServiceTests
    {
        private static readonly DateTimeOffset NowUtc = new(2026, 8, 5, 6, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Decide_AllowsMatchingEnabledGrant()
        {
            LicenseDecisionResult result = Service().Decide(Context());
            Assert.Equal(LicenseDecision.Allowed, result.Decision);
            Assert.Contains(LicenseFeature.InstallAndMaintenance, result.Features);
        }

        [Fact]
        public void Decide_RejectsGrantForAnotherClient()
        {
            LicenseDecisionContext context = Context();
            context.Grant.ClientId = "another-client";
            Assert.Equal(LicenseDecision.ClientNotFound, Service().Decide(context).Decision);
        }

        [Fact]
        public void Decide_RejectsGrantForAnotherDevice()
        {
            LicenseDecisionContext context = Context();
            context.Grant.DeviceId = "another-device";
            Assert.Equal(LicenseDecision.DeviceNotRegistered, Service().Decide(context).Decision);
        }

        [Fact]
        public void Decide_RejectsDisabledClientAndDevice()
        {
            LicenseDecisionContext client = Context();
            client.Grant.ClientEnabled = false;
            Assert.Equal(LicenseDecision.ClientDisabled, Service().Decide(client).Decision);

            LicenseDecisionContext device = Context();
            device.Grant.DeviceEnabled = false;
            Assert.Equal(LicenseDecision.DeviceDisabled, Service().Decide(device).Decision);
        }

        [Fact]
        public void Decide_OnlinePolicyFalseOverridesStaleEnabledGrant()
        {
            LicenseDecisionContext context = Context();
            context.Grant.ClientEnabled = true;
            context.OnlineClientPolicyEnabled = false;

            Assert.Equal(LicenseDecision.ClientDisabled, Service().Decide(context).Decision);
        }

        [Fact]
        public void Decide_OnlinePolicyTrueOverridesStaleDisabledGrant()
        {
            LicenseDecisionContext context = Context();
            context.Grant.ClientEnabled = false;
            context.OnlineClientPolicyEnabled = true;

            Assert.Equal(LicenseDecision.Allowed, Service().Decide(context).Decision);
        }

        [Fact]
        public void Decide_OfflineCacheStillUsesSignedClientEnabledSnapshot()
        {
            LicenseDecisionContext context = Context();
            context.ManifestSource = LicenseManifestSource.Cache;
            context.LastSuccessfulOnlineCheckUtc = NowUtc.AddHours(-1);
            context.Grant.ClientEnabled = false;
            context.OnlineClientPolicyEnabled = true;

            Assert.Equal(LicenseDecision.ClientDisabled, Service().Decide(context).Decision);
        }

        [Fact]
        public void Decide_OnlineMissingPolicyPreservesLegacyGrantSemantics()
        {
            LicenseDecisionContext context = Context();
            context.Grant.ClientEnabled = false;
            context.OnlineClientPolicyEnabled = null;

            Assert.Equal(LicenseDecision.ClientDisabled, Service().Decide(context).Decision);
        }

        [Fact]
        public void Decide_RejectsExpiredGrant()
        {
            LicenseDecisionContext context = Context();
            context.Grant.ValidUntilUtc = NowUtc.AddTicks(-1);
            Assert.Equal(LicenseDecision.ManifestExpired, Service().Decide(context).Decision);
        }

        [Fact]
        public void Decide_RejectsExpiredOfflineGrace()
        {
            LicenseDecisionContext context = Context();
            context.ManifestSource = LicenseManifestSource.Cache;
            context.LastSuccessfulOnlineCheckUtc = NowUtc.AddHours(-25);
            context.Grant.OfflineGraceHours = 24;
            LicenseDecisionResult result = Service().Decide(context);
            Assert.Equal(LicenseDecision.OfflineGraceExpired, result.Decision);
            Assert.Equal(NowUtc.AddHours(-1), result.OfflineGraceEndsAtUtc);
        }

        [Fact]
        public void Decide_OperatorGrantKeepsLegacyFeaturesWithoutServiceEscalation()
        {
            LicenseDecisionContext context = Context();
            context.Grant.OperatorDevice = true;
            context.Grant.Features.Clear();
            LicenseDecisionResult result = Service().Decide(context);
            Assert.Equal(LicenseDecision.Allowed, result.Decision);
            Assert.Equal(2, result.Features.Count);
            Assert.Contains(LicenseFeature.ViewAndRepair, result.Features);
            Assert.Contains(LicenseFeature.InstallAndMaintenance, result.Features);
            Assert.DoesNotContain(LicenseFeature.Service, result.Features);
        }

        [Fact]
        public void Decide_PreservesExplicitServiceFeature()
        {
            LicenseDecisionContext context = Context();
            context.Grant.Features = new List<LicenseFeature> { LicenseFeature.Service };

            LicenseDecisionResult result = Service().Decide(context);

            Assert.Equal(LicenseDecision.Allowed, result.Decision);
            Assert.Equal(new[] { LicenseFeature.Service }, result.Features);
        }

        private static LicenseDecisionService Service() =>
            new(new LicenseDecisionPolicy(), () => NowUtc);

        private static LicenseDecisionContext Context() => new()
        {
            ClientId = "client-1",
            DeviceId = "device-1",
            CurrentHonestFlowVersion = new Version(3, 0, 0),
            ManifestSource = LicenseManifestSource.Remote,
            Grant = new LicenseGrant
            {
                SchemaVersion = 1,
                Revision = 4,
                ClientId = "client-1",
                DeviceId = "device-1",
                ClientEnabled = true,
                DeviceEnabled = true,
                MinHonestFlowVersion = "3.0.0",
                OfflineGraceHours = 24,
                IssuedAtUtc = NowUtc.AddHours(-1),
                ValidUntilUtc = NowUtc.AddDays(7),
                Features = new List<LicenseFeature>
                {
                    LicenseFeature.ViewAndRepair,
                    LicenseFeature.InstallAndMaintenance
                }
            }
        };
    }
}
