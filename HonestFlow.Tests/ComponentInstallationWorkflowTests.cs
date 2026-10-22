using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Installation.Planning;
using HonestFlow.Application.Lm;
using HonestFlow.Application.Core;
using HonestFlow.Application.PointStatus;
using HonestFlow.Application.Licensing;
using HonestFlow.Models;
using HonestFlow.Models.Licensing;
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
        public async Task InstallAsync_DoesNotStartTsPiotRegistration()
        {
            var registrationClient = new StubRegistrationClient(TsPiotRegistrationResult.Success());
            var workflow = new ComponentInstallationWorkflow(
                new StubInstallationService { InstallResult = true },
                () => true,
                () => new LmSystemRequirementsResult(Array.Empty<string>(), Array.Empty<string>()),
                RegistrationWorkflow(registrationClient));

            bool result = await workflow.InstallAsync(new IPData(), CancellationToken.None);

            Assert.True(result);
            Assert.False(registrationClient.Called);
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
        public async Task InstallAsync_PassesSkipLmStackOptionToInstallationService()
        {
            var installation = new StubInstallationService { InstallResult = true };
            var workflow = new ComponentInstallationWorkflow(
                installation,
                () => true,
                () => new LmSystemRequirementsResult(Array.Empty<string>(), Array.Empty<string>()));
            var options = new InstallationOptions { SkipLmStack = true };

            await workflow.InstallAsync(new IPData(), CancellationToken.None, options);

            Assert.Same(options, installation.Options);
            Assert.True(installation.Options.SkipLmStack);
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

        [Fact]
        public async Task InstallAndRegisterTsPiotAsync_SeparatesSuccessfulInstallationFromRegistrationFailure()
        {
            var installation = new StubInstallationService { InstallResult = true };
            var registrationClient = new StubRegistrationClient(TsPiotRegistrationResult.KktNotDetected());
            var workflow = new ComponentInstallationWorkflow(
                installation,
                () => true,
                () => new LmSystemRequirementsResult(Array.Empty<string>(), Array.Empty<string>()),
                RegistrationWorkflow(registrationClient));

            ComponentInstallationCompletionResult result = await workflow.InstallAndRegisterTsPiotAsync(
                new IPData(), CancellationToken.None);

            Assert.True(result.ComponentsInstalled);
            Assert.Equal(TsPiotRegistrationStatus.KktNotDetected, result.TsPiotRegistration.Status);
            Assert.True(registrationClient.Called);
        }

        [Fact]
        public async Task InstallAndRegisterTsPiotAsync_DoesNotRegisterWhenInstallationFails()
        {
            var registrationClient = new StubRegistrationClient(TsPiotRegistrationResult.Success());
            var workflow = new ComponentInstallationWorkflow(
                new StubInstallationService { InstallResult = false },
                () => true,
                () => new LmSystemRequirementsResult(Array.Empty<string>(), Array.Empty<string>()),
                RegistrationWorkflow(registrationClient));

            ComponentInstallationCompletionResult result = await workflow.InstallAndRegisterTsPiotAsync(
                new IPData(), CancellationToken.None);

            Assert.False(result.ComponentsInstalled);
            Assert.Null(result.TsPiotRegistration);
            Assert.False(registrationClient.Called);
        }

        [Fact]
        public async Task InstallAndRegisterTsPiotAsync_LmOnlyRepair_DoesNotRegisterTsPiot()
        {
            var registrationClient = new StubRegistrationClient(TsPiotRegistrationResult.Success());
            var workflow = new ComponentInstallationWorkflow(
                new StubInstallationService { InstallResult = true },
                () => true,
                () => new LmSystemRequirementsResult(Array.Empty<string>(), Array.Empty<string>()),
                RegistrationWorkflow(registrationClient));

            ComponentInstallationCompletionResult result = await workflow.InstallAndRegisterTsPiotAsync(
                new IPData(),
                CancellationToken.None,
                new InstallationOptions { RequestedComponents = new[] { InstallationComponent.LmModule } });

            Assert.True(result.ComponentsInstalled);
            Assert.Null(result.TsPiotRegistration);
            Assert.False(registrationClient.Called);
        }

        private static TsPiotRegistrationWorkflow RegistrationWorkflow(TsPiotRegistrationResult result) =>
            RegistrationWorkflow(new StubRegistrationClient(result));

        [Theory]
        [InlineData("cancel", ComponentPostInstallationStatus.Cancelled)]
        [InlineData("denied", ComponentPostInstallationStatus.Denied)]
        [InlineData("timeout", ComponentPostInstallationStatus.Failed)]
        [InlineData("error", ComponentPostInstallationStatus.Failed)]
        public async Task PostInstallationException_PreservesInstalledComponents(string outcome, ComponentPostInstallationStatus status)
        {
            using var cancellation = new CancellationTokenSource();
            Exception error = outcome switch
            {
                "cancel" => new OperationCanceledException("cancelled", cancellation.Token),
                "denied" => new LicenseOperationDeniedException(LicenseOperation.RegisterTsPiot, null),
                "timeout" => new TaskCanceledException("HTTP timeout"),
                _ => new InvalidOperationException("private technical error")
            };
            var registration = new StubRegistrationClient(null) { Error = error };
            var workflow = new ComponentInstallationWorkflow(new StubInstallationService { InstallResult = true },
                () => true, () => null, RegistrationWorkflow(registration));

            ComponentInstallationCompletionResult result = await workflow.InstallAndRegisterTsPiotAsync(new IPData(), cancellation.Token);

            Assert.True(registration.Called);
            Assert.True(result.ComponentsInstalled);
            Assert.False(result.IsSuccessful);
            Assert.Equal(status, result.PostInstallationStatus);
            Assert.StartsWith("Компоненты установлены.", result.PostInstallationUserMessage);
            Assert.DoesNotContain("private technical error", result.PostInstallationUserMessage);
        }

        [Fact]
        public async Task CancellationImmediatelyAfterInstallation_DoesNotStartRegistration()
        {
            using var cancellation = new CancellationTokenSource();
            var registration = new StubRegistrationClient(TsPiotRegistrationResult.Success());
            var installation = new StubInstallationService { InstallResult = true, AfterInstall = cancellation.Cancel };
            var workflow = new ComponentInstallationWorkflow(installation, () => true, () => null, RegistrationWorkflow(registration));

            ComponentInstallationCompletionResult result = await workflow.InstallAndRegisterTsPiotAsync(new IPData(), cancellation.Token);

            Assert.True(result.ComponentsInstalled);
            Assert.Equal(ComponentPostInstallationStatus.Cancelled, result.PostInstallationStatus);
            Assert.False(registration.Called);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task InstallationException_IsNotReportedAsPartialSuccess(bool cancelled)
        {
            Exception error = cancelled ? new OperationCanceledException() : new InvalidOperationException("installer failed");
            var registration = new StubRegistrationClient(TsPiotRegistrationResult.Success());
            var workflow = new ComponentInstallationWorkflow(new StubInstallationService { Error = error },
                () => true, () => null, RegistrationWorkflow(registration));

            Exception actual = await Record.ExceptionAsync(() => workflow.InstallAndRegisterTsPiotAsync(new IPData(), CancellationToken.None));

            Assert.Same(error, actual);
            Assert.False(registration.Called);
        }

        private static TsPiotRegistrationWorkflow RegistrationWorkflow(StubRegistrationClient client) =>
            new(client, new StubPortProbe(), new StubLog(), new AllowLicenseOperationGuard());

        private static ComponentInstallationWorkflow CreateWorkflow(bool isAdministrator) =>
            new(
                new StubInstallationService(),
                () => isAdministrator,
                () => new LmSystemRequirementsResult(Array.Empty<string>(), Array.Empty<string>()));

        private sealed class StubInstallationService : IInstallationService
        {
            public Exception Error { get; init; }
            public Action AfterInstall { get; init; }
            public bool InstallResult { get; init; }
            public bool ReinstallResult { get; init; }
            public IPData InstalledClient { get; private set; }
            public InstallationOptions Options { get; private set; }
            public CancellationToken InstallationCancellationToken { get; private set; }
            public CancellationToken ReinstallCancellationToken { get; private set; }
            public IReadOnlyCollection<InstallationComponent> ReinstalledComponents { get; private set; }

            public Task<bool> CheckLmAndInstall(
                IPData selectedIP,
                InstallationOptions options = null,
                CancellationToken cancellationToken = default)
            {
                InstalledClient = selectedIP;
                Options = options;
                InstallationCancellationToken = cancellationToken;
                if (Error != null) return Task.FromException<bool>(Error);
                AfterInstall?.Invoke();
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

        private sealed class StubRegistrationClient : IEsmTsPiotRegistrationClient
        {
            public Exception Error { get; init; }
            private readonly TsPiotRegistrationResult _result;
            public StubRegistrationClient(TsPiotRegistrationResult result) => _result = result;
            public bool Called { get; private set; }
            public Task<TsPiotRegistrationResult> RegisterAsync(CancellationToken cancellationToken)
            {
                Called = true;
                if (Error != null) return Task.FromException<TsPiotRegistrationResult>(Error);
                return Task.FromResult(_result);
            }
        }

        private sealed class StubPortProbe : IEsmApiPortProbe
        {
            public Task<EsmApiPortProbeResult> CheckAsync(CancellationToken cancellationToken) =>
                Task.FromResult(EsmApiPortProbeResult.Available(51888));
        }

        private sealed class StubLog : ILogService
        {
            public void LogUser(string message, bool isError = false) { }
            public void LogDebug(string message) { }
            public string GetUserLog() => string.Empty;
        }
    }
}
