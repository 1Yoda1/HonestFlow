using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Installation.Planning;
using HonestFlow.Application.Lm;
using HonestFlow.Infrastructure;
using HonestFlow.Models;

namespace HonestFlow.Application.Installation
{
    public sealed class ComponentInstallationWorkflow
    {
        private readonly IInstallationService _installationService;
        private readonly Func<bool> _isAdministrator;
        private readonly Func<LmSystemRequirementsResult> _checkSystemRequirements;
        private readonly TsPiotRegistrationWorkflow _tsPiotRegistrationWorkflow;

        public ComponentInstallationWorkflow(IInstallationService installationService)
            : this(installationService, Utils.IsAdministrator, LmSystemRequirements.Check)
        {
        }

        public ComponentInstallationWorkflow(
            IInstallationService installationService,
            Func<bool> isAdministrator,
            Func<LmSystemRequirementsResult> checkSystemRequirements,
            TsPiotRegistrationWorkflow tsPiotRegistrationWorkflow = null)
        {
            _installationService = installationService ?? throw new ArgumentNullException(nameof(installationService));
            _isAdministrator = isAdministrator ?? throw new ArgumentNullException(nameof(isAdministrator));
            _checkSystemRequirements = checkSystemRequirements ?? throw new ArgumentNullException(nameof(checkSystemRequirements));
            _tsPiotRegistrationWorkflow = tsPiotRegistrationWorkflow;
        }

        public ComponentOperationReadiness CheckReadiness(IPData selectedClient, bool checkLmRequirements)
        {
            if (selectedClient == null)
                return ComponentOperationReadiness.ClientRequired();
            if (!_isAdministrator())
                return ComponentOperationReadiness.AdministratorRequired();

            return ComponentOperationReadiness.Ready(
                checkLmRequirements ? _checkSystemRequirements() : null);
        }

        public Task<bool> InstallAsync(IPData selectedClient, CancellationToken cancellationToken, InstallationOptions options = null) =>
            _installationService.CheckLmAndInstall(selectedClient, options, cancellationToken);

        public async Task<ComponentInstallationCompletionResult> InstallAndRegisterTsPiotAsync(
            IPData selectedClient,
            CancellationToken cancellationToken,
            InstallationOptions options = null)
        {
            bool installed = await InstallAsync(selectedClient, cancellationToken, options).ConfigureAwait(false);
            if (!installed)
                return ComponentInstallationCompletionResult.InstallationFailed();

            if (_tsPiotRegistrationWorkflow == null)
                return ComponentInstallationCompletionResult.Installed(null);

            TsPiotRegistrationResult registration = await _tsPiotRegistrationWorkflow.RegisterAsync(cancellationToken)
                .ConfigureAwait(false);
            return ComponentInstallationCompletionResult.Installed(registration);
        }

        public Task<bool> ReinstallAsync(
            IPData selectedClient,
            IReadOnlyCollection<InstallationComponent> components,
            CancellationToken cancellationToken = default) =>
            _installationService.ReinstallSelectedComponents(selectedClient, components, cancellationToken);
    }

    public enum ComponentOperationReadinessStatus
    {
        Ready,
        ClientRequired,
        AdministratorRequired
    }

    public sealed class ComponentOperationReadiness
    {
        private ComponentOperationReadiness(
            ComponentOperationReadinessStatus status,
            LmSystemRequirementsResult systemRequirements)
        {
            Status = status;
            SystemRequirements = systemRequirements;
        }

        public ComponentOperationReadinessStatus Status { get; }
        public LmSystemRequirementsResult SystemRequirements { get; }
        public bool CanContinue => Status == ComponentOperationReadinessStatus.Ready;

        public static ComponentOperationReadiness Ready(LmSystemRequirementsResult requirements) =>
            new(ComponentOperationReadinessStatus.Ready, requirements);

        public static ComponentOperationReadiness ClientRequired() =>
            new(ComponentOperationReadinessStatus.ClientRequired, null);

        public static ComponentOperationReadiness AdministratorRequired() =>
            new(ComponentOperationReadinessStatus.AdministratorRequired, null);
    }
}
