using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Installation.Planning;
using HonestFlow.Application.Lm;
using HonestFlow.Models;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class ComponentInstallationWorkflowTests
    {
        [Fact]
        public void CheckReadiness_RequiresClientBeforeSystemChanges()
        {
            var workflow = CreateWorkflow(isAdministrator: true);

            ComponentOperationReadiness result = workflow.CheckReadiness(null, true);

            Assert.Equal(ComponentOperationReadinessStatus.ClientRequired, result.Status);
        }

        [Fact]
        public void CheckReadiness_ReturnsRequirementsForAuthorizedAdministrator()
        {
            var requirements = new LmSystemRequirementsResult(
                new[] { "Недостаточно памяти" },
                Array.Empty<string>());
            var workflow = new ComponentInstallationWorkflow(
                new StubInstallationService(),
                () => true,
                () => requirements);

            ComponentOperationReadiness result = workflow.CheckReadiness(new IPData(), true);

            Assert.True(result.CanContinue);
            Assert.Same(requirements, result.SystemRequirements);
        }

        [Fact]
        public async Task InstallAsync_DelegatesToInstallationService()
        {
            var installation = new StubInstallationService { InstallResult = true };
            var workflow = new ComponentInstallationWorkflow(
                installation,
                () => true,
                () => new LmSystemRequirementsResult(Array.Empty<string>(), Array.Empty<string>()));
            var client = new IPData { ClientId = "client-1" };

            bool result = await workflow.InstallAsync(client, CancellationToken.None);

            Assert.True(result);
            Assert.Same(client, installation.InstalledClient);
        }

        [Fact]
        public async Task InstallAsync_PassesCancellationTokenToInstallationService()
        {
            var installation = new StubInstallationService { InstallResult = true };
            var workflow = new ComponentInstallationWorkflow(
                installation,
                () => true,
                () => new LmSystemRequirementsResult(Array.Empty<string>(), Array.Empty<string>()));
            using var cancellation = new CancellationTokenSource();

            await workflow.InstallAsync(new IPData(), cancellation.Token);

            Assert.Equal(cancellation.Token, installation.InstallationCancellationToken);
        }

        [Fact]
        public async Task ReinstallAsync_PassesSelectionAndCancellationTokenToInstallationService()
        {
            var installation = new StubInstallationService { ReinstallResult = true };
            var workflow = new ComponentInstallationWorkflow(
                installation,
                () => true,
                () => new LmSystemRequirementsResult(Array.Empty<string>(), Array.Empty<string>()));
            using var cancellation = new CancellationTokenSource();
            InstallationComponent[] selected = { InstallationComponent.LmModule };

            bool result = await workflow.ReinstallAsync(new IPData(), selected, cancellation.Token);

            Assert.True(result);
            Assert.Equal(cancellation.Token, installation.ReinstallCancellationToken);
            Assert.Equal(selected, installation.ReinstalledComponents);
        }

        private static ComponentInstallationWorkflow CreateWorkflow(bool isAdministrator) =>
            new(
                new StubInstallationService(),
                () => isAdministrator,
                () => new LmSystemRequirementsResult(Array.Empty<string>(), Array.Empty<string>()));

        private sealed class StubInstallationService : IInstallationService
        {
            public bool InstallResult { get; init; }
            public bool ReinstallResult { get; init; }
            public IPData InstalledClient { get; private set; }
            public CancellationToken InstallationCancellationToken { get; private set; }
            public CancellationToken ReinstallCancellationToken { get; private set; }
            public IReadOnlyCollection<InstallationComponent> ReinstalledComponents { get; private set; }

            public Task<bool> CheckLmAndInstall(
                IPData selectedIP,
                CancellationToken cancellationToken = default)
            {
                InstalledClient = selectedIP;
                InstallationCancellationToken = cancellationToken;
                return Task.FromResult(InstallResult);
            }

            public Task<bool> ReinstallSelectedComponents(
                IPData selectedIP,
                IReadOnlyCollection<InstallationComponent> components,
                CancellationToken cancellationToken = default)
            {
                ReinstalledComponents = components;
                ReinstallCancellationToken = cancellationToken;
                return Task.FromResult(ReinstallResult);
            }
        }
    }
}
