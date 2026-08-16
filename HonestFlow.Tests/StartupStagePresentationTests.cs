using System.Collections.Generic;
using HonestFlow.Application.Bootstrap;
using HonestFlow.Application.Licensing;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class StartupStagePresentationTests
    {
        [Theory]
        [MemberData(nameof(RequiredMappings))]
        public void RequiredScenario_HasExpectedFiveStageMapping(
            StartupPresentationPhase phase,
            StartupStageVisualState preparation,
            StartupStageVisualState access,
            StartupStageVisualState device,
            StartupStageVisualState license,
            StartupStageVisualState launch)
        {
            StartupStagePresentation result = StartupStagePresentationMapper.Create(phase);

            Assert.Equal(preparation, result.Preparation);
            Assert.Equal(access, result.Access);
            Assert.Equal(device, result.Device);
            Assert.Equal(license, result.License);
            Assert.Equal(launch, result.Launch);
        }

        public static IEnumerable<object[]> RequiredMappings()
        {
            yield return Row(StartupPresentationPhase.FreshLogin, Complete, Active, Inactive, Inactive, Inactive);
            yield return Row(StartupPresentationPhase.ClientAccessDisabled, Complete, Error, Inactive, Inactive, Inactive);
            yield return Row(StartupPresentationPhase.DeviceAwaitingAddress, Complete, Complete, Active, Inactive, Inactive);
            yield return Row(StartupPresentationPhase.DevicePending, Complete, Complete, Active, Inactive, Inactive);
            yield return Row(StartupPresentationPhase.DeviceRejected, Complete, Complete, Error, Inactive, Inactive);
            yield return Row(StartupPresentationPhase.LicenseNotIssued, Complete, Complete, Complete, Active, Inactive);
            yield return Row(StartupPresentationPhase.AllowedOnline, Complete, Complete, Complete, Complete, Active);
            yield return Row(StartupPresentationPhase.AllowedOffline, Complete, Complete, Complete, Complete, Active);
            yield return Row(StartupPresentationPhase.LicenseDenied, Complete, Complete, Complete, Error, Inactive);
            yield return Row(StartupPresentationPhase.DeviceStatusUnavailable, Complete, Complete, Active, Inactive, Inactive);
        }

        [Theory]
        [InlineData(LicenseDecision.OfflineGraceExpired)]
        [InlineData(LicenseDecision.ManifestExpired)]
        [InlineData(LicenseDecision.InvalidLicenseState)]
        [InlineData(LicenseDecision.ClientDisabled)]
        public void HardLicenseDenial_MapsToLicenseError(LicenseDecision decision)
        {
            StartupPresentationPhase phase = StartupStagePresentationMapper.PhaseForLicense(
                new LicenseObservationSnapshot { Decision = decision });

            StartupStagePresentation result = StartupStagePresentationMapper.Create(phase);
            Assert.Equal(Complete, result.Preparation);
            Assert.Equal(Complete, result.Access);
            Assert.Equal(Complete, result.Device);
            Assert.Equal(Error, result.License);
            Assert.Equal(Inactive, result.Launch);
        }

        [Fact]
        public void ClientPolicyOff_MapsToAccessErrorInsteadOfLicenseError()
        {
            StartupPresentationPhase phase = StartupStagePresentationMapper.PhaseForLicense(
                new LicenseObservationSnapshot
                {
                    Decision = LicenseDecision.ClientDisabled,
                    TechnicalCode = "CLIENT_ACCESS_DISABLED"
                });

            StartupStagePresentation result = StartupStagePresentationMapper.Create(phase);
            Assert.Equal(Error, result.Access);
            Assert.Equal(Inactive, result.Device);
            Assert.Equal(Inactive, result.License);
        }

        [Theory]
        [InlineData(LicenseManifestSource.Remote, StartupPresentationPhase.AllowedOnline)]
        [InlineData(LicenseManifestSource.Cache, StartupPresentationPhase.AllowedOffline)]
        public void Allowed_MapsOnlineAndOfflineToLaunchReady(
            LicenseManifestSource source,
            StartupPresentationPhase expectedPhase)
        {
            StartupPresentationPhase phase = StartupStagePresentationMapper.PhaseForLicense(
                new LicenseObservationSnapshot
                {
                    Decision = LicenseDecision.Allowed,
                    ManifestSource = source
                });

            Assert.Equal(expectedPhase, phase);
            Assert.Equal(Active, StartupStagePresentationMapper.Create(phase).Launch);
        }

        [Theory]
        [InlineData(DeviceRegistrationStartupState.AwaitingAddress, StartupPresentationPhase.DeviceAwaitingAddress)]
        [InlineData(DeviceRegistrationStartupState.Pending, StartupPresentationPhase.DevicePending)]
        [InlineData(DeviceRegistrationStartupState.Rejected, StartupPresentationPhase.DeviceRejected)]
        [InlineData(DeviceRegistrationStartupState.StatusUnavailable, StartupPresentationPhase.DeviceStatusUnavailable)]
        public void RegistrationState_MapsToExpectedDeviceStage(
            DeviceRegistrationStartupState state,
            StartupPresentationPhase expectedPhase)
        {
            Assert.Equal(expectedPhase, StartupStagePresentationMapper.PhaseForRegistration(state));
        }

        [Fact]
        public void LicenseNotIssued_MapsToActiveLicenseStage()
        {
            StartupPresentationPhase phase = StartupStagePresentationMapper.PhaseForLicense(
                new LicenseObservationSnapshot { Decision = LicenseDecision.LicenseNotIssued });

            StartupStagePresentation result = StartupStagePresentationMapper.Create(phase);
            Assert.Equal(Complete, result.Device);
            Assert.Equal(Active, result.License);
            Assert.Equal(Inactive, result.Launch);
        }

        private static object[] Row(
            StartupPresentationPhase phase,
            StartupStageVisualState preparation,
            StartupStageVisualState access,
            StartupStageVisualState device,
            StartupStageVisualState license,
            StartupStageVisualState launch) =>
            [phase, preparation, access, device, license, launch];

        private const StartupStageVisualState Inactive = StartupStageVisualState.Inactive;
        private const StartupStageVisualState Active = StartupStageVisualState.Active;
        private const StartupStageVisualState Complete = StartupStageVisualState.Complete;
        private const StartupStageVisualState Error = StartupStageVisualState.Error;
    }
}
