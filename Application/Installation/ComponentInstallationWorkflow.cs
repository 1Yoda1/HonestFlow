using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Installation.Planning;
using HonestFlow.Application.Lm;
using HonestFlow.Application.Licensing;
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
        private readonly KktBootstrapWorkflow _kktBootstrapWorkflow;

        public ComponentInstallationWorkflow(IInstallationService installationService)
            : this(installationService, Utils.IsAdministrator, LmSystemRequirements.Check)
        {
        }

        public ComponentInstallationWorkflow(
            IInstallationService installationService,
            Func<bool> isAdministrator,
            Func<LmSystemRequirementsResult> checkSystemRequirements,
            TsPiotRegistrationWorkflow tsPiotRegistrationWorkflow = null,
            KktBootstrapWorkflow kktBootstrapWorkflow = null)
        {
            _installationService = installationService ?? throw new ArgumentNullException(nameof(installationService));
            _isAdministrator = isAdministrator ?? throw new ArgumentNullException(nameof(isAdministrator));
            _checkSystemRequirements = checkSystemRequirements ?? throw new ArgumentNullException(nameof(checkSystemRequirements));
            _tsPiotRegistrationWorkflow = tsPiotRegistrationWorkflow;
            _kktBootstrapWorkflow = kktBootstrapWorkflow;
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

            // Installation has already succeeded. A later cancellation/registration error
            // must not erase that result or suggest that the installers need to run again.
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_kktBootstrapWorkflow != null)
                {
                    KktBootstrapResult bootstrap = await _kktBootstrapWorkflow
                        .RunAsync(selectedClient, cancellationToken)
                        .ConfigureAwait(false);
                    return ComponentInstallationCompletionResult.InstalledWithBootstrap(bootstrap);
                }

                if (_tsPiotRegistrationWorkflow == null)
                    return ComponentInstallationCompletionResult.Installed(null);

                TsPiotRegistrationResult registration = await _tsPiotRegistrationWorkflow.RegisterAsync(cancellationToken)
                    .ConfigureAwait(false);
                return ComponentInstallationCompletionResult.Installed(registration);
            }
            catch (LicenseOperationDeniedException ex)
            {
                Logger.Warning($"Компоненты установлены; последующая настройка заблокирована: {ex.TechnicalCode}", nameof(ComponentInstallationWorkflow));
                return ComponentInstallationCompletionResult.PostInstallationDenied();
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested ||
                (cancellationToken.CanBeCanceled && ex.CancellationToken == cancellationToken))
            {
                Logger.Info("Компоненты установлены; последующая настройка отменена.", nameof(ComponentInstallationWorkflow));
                return ComponentInstallationCompletionResult.PostInstallationCancelled();
            }
            catch (Exception ex)
            {
                Logger.DebugLog($"Ошибка последующей настройки после установки компонентов: {ex}", nameof(ComponentInstallationWorkflow));
                return ComponentInstallationCompletionResult.PostInstallationFailed();
            }
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
