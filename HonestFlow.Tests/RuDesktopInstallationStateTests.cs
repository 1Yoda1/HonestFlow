using HonestFlow.Application.RemoteAccess;
using HonestFlow.Application.PointStatus;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class RuDesktopInstallationStateTests
    {
        [Theory]
        [InlineData(false, false, false, RuDesktopInstallationState.NotInstalled)]
        [InlineData(true, false, false, RuDesktopInstallationState.Damaged)]
        [InlineData(false, true, true, RuDesktopInstallationState.Damaged)]
        [InlineData(true, true, false, RuDesktopInstallationState.ServiceStopped)]
        [InlineData(true, true, true, RuDesktopInstallationState.Ready)]
        public void ClassifyInstallationState_ReturnsExpectedState(
            bool executableFound,
            bool serviceInstalled,
            bool serviceRunning,
            RuDesktopInstallationState expected)
        {
            RuDesktopInstallationState actual = RuDesktopService.ClassifyInstallationState(
                executableFound,
                serviceInstalled,
                serviceRunning);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(NodeActionKind.InstallRuDesktop, "Установить")]
        [InlineData(NodeActionKind.ReinstallRuDesktop, "Переустановить")]
        [InlineData(NodeActionKind.RequestRuDesktopHelp, "Запросить помощь")]
        public void RuDesktopAction_UsesExpectedButtonText(NodeActionKind actionKind, string expected)
        {
            var status = new NodeStatus(
                NodeLevel.Warning,
                "test",
                "test",
                actionKind: actionKind);

            Assert.Equal(expected, status.ActionText);
        }

        [Fact]
        public void StoppedService_UsesStartButtonText()
        {
            var status = new NodeStatus(
                NodeLevel.Warning,
                "test",
                "test",
                services: new[] { new ServiceSnapshot("RuDesktop", "Stopped") },
                actionKind: NodeActionKind.ManageServices);

            Assert.Equal("Запустить", status.ActionText);
        }

        [Fact]
        public void ResetInstallationDependentState_ClearsStaleInstallationData()
        {
            var lastClient = new LastAuthorizedClientState { Name = "ИП Тест" };
            var state = new RuDesktopLocalState
            {
                LastKnownId = "123456789",
                PasswordConfiguredByHonestFlow = true,
                PasswordFingerprint = "fingerprint",
                PasswordConfiguredAt = System.DateTime.Now,
                SuppressPasswordSetupPrompt = true,
                LastAuthorizedClient = lastClient
            };

            RuDesktopService.ResetInstallationDependentState(state);

            Assert.Null(state.LastKnownId);
            Assert.False(state.PasswordConfiguredByHonestFlow);
            Assert.Null(state.PasswordFingerprint);
            Assert.Null(state.PasswordConfiguredAt);
            Assert.False(state.SuppressPasswordSetupPrompt);
            Assert.Same(lastClient, state.LastAuthorizedClient);
        }
    }
}
