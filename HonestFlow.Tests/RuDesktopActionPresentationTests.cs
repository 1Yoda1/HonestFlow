using HonestFlow.Application.RemoteAccess;
using Xunit;

namespace HonestFlow.Tests;

public sealed class RuDesktopActionPresentationTests
{
    [Theory]
    [InlineData(RuDesktopInstallationState.NotInstalled, RuDesktopActionKind.Install)]
    [InlineData(RuDesktopInstallationState.Damaged, RuDesktopActionKind.Reinstall)]
    [InlineData(RuDesktopInstallationState.ServiceStopped, RuDesktopActionKind.StartService)]
    [InlineData(RuDesktopInstallationState.CheckFailed, RuDesktopActionKind.Refresh)]
    public void FaultState_SelectsRecoveryAction(
        RuDesktopInstallationState state,
        RuDesktopActionKind expected)
    {
        var status = new RuDesktopStatus { InstallationState = state };

        Assert.Equal(expected, RuDesktopActionPresentation.Create(status, enabledForClient: true).Action);
    }

    [Fact]
    public void ReadyState_OffersSetupUntilCredentialIsSynchronized()
    {
        var status = new RuDesktopStatus
        {
            InstallationState = RuDesktopInstallationState.Ready,
            Id = "123456789",
            PasswordConfiguredByHonestFlow = true
        };

        RuDesktopActionPresentation action = RuDesktopActionPresentation.Create(status, enabledForClient: true);

        Assert.Equal(RuDesktopActionKind.SetupPassword, action.Action);
        Assert.True(action.IsEnabled);
        status.RemoteCredentialSynced = true;
        Assert.Equal(RuDesktopActionKind.RequestHelp,
            RuDesktopActionPresentation.Create(status, enabledForClient: true).Action);
        status.RevocationPending = true;
        Assert.Equal(RuDesktopActionKind.None,
            RuDesktopActionPresentation.Create(status, enabledForClient: true).Action);
    }

    [Fact]
    public void ClientOptOut_BlocksInstallation()
    {
        var missing = new RuDesktopStatus { InstallationState = RuDesktopInstallationState.NotInstalled };

        Assert.Equal(RuDesktopActionKind.None,
            RuDesktopActionPresentation.Create(missing, enabledForClient: false).Action);
    }

    [Fact]
    public void RebootRequired_BlocksFurtherActions()
    {
        var stopped = new RuDesktopStatus { InstallationState = RuDesktopInstallationState.ServiceStopped };

        Assert.Equal(RuDesktopActionKind.None,
            RuDesktopActionPresentation.Create(stopped, enabledForClient: true, rebootRequired: true).Action);
    }
}
