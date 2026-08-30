using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using HonestFlow.Application.Bootstrap;
using HonestFlow.Application.Core;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Installation.Planning;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.Lm;
using HonestFlow.Application.PointStatus;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Models;
using HonestFlow.Models.Licensing;

namespace HonestFlow.UI;

internal static class WpfAutoFixComposition
{
    public static AutoFixWorkflow Create(
        Window owner,
        StartupResult startup,
        IPData client,
        ILogService logService,
        PointStatusRefreshService pointStatusRefresh,
        IProgressService installationProgress,
        Func<LmSystemRequirementsResult?, InstallationOptions?> resolveInstallationOptions,
        Func<ILicenseOperationGuard> createLicenseGuard,
        Action<PointStatusRefreshResult> applyRefresh)
    {
        var dialogs = new AutoFixDialogs(owner);
        var installationService = new InstallationService(
            logService,
            installationProgress,
            dialogs,
            createLicenseGuard(),
            startup.UseRemoteConfigMode);
        var registration = new TsPiotRegistrationWorkflow(
            new EsmTsPiotRegistrationClient(),
            new EsmApiPortProbe(),
            logService);
        var bootstrap = WpfKktBootstrapComposition.Create(
            owner,
            startup,
            client,
            registration,
            pointStatusRefresh,
            installationProgress,
            createLicenseGuard,
            applyRefresh);
        var installation = new ComponentInstallationWorkflow(
            installationService,
            Utils.IsAdministrator,
            LmSystemRequirements.Check,
            registration,
            bootstrap);
        var pointRepair = new PointRepairWorkflow(
            new WindowsServiceControlService(createLicenseGuard()),
            new LicensedLmInitializationService(createLicenseGuard()));

        var actions = new Dictionary<DiagnosticFixKey, AutoFixAction>
        {
            [DiagnosticFixKey.RunSmartInstallation] = async (_, _, token) =>
            {
                ComponentOperationReadiness readiness = installation.CheckReadiness(client, checkLmRequirements: true);
                if (!readiness.CanContinue)
                    throw new InvalidOperationException(readiness.Status == ComponentOperationReadinessStatus.AdministratorRequired
                        ? "Запустите HonestFlow от имени администратора."
                        : "Не выбрана рабочая точка.");
                InstallationOptions? options = owner.Dispatcher.Invoke(
                    () => resolveInstallationOptions(readiness.SystemRequirements));
                if (options == null) throw new OperationCanceledException(token);
                ComponentInstallationCompletionResult completion =
                    await installation.InstallAndRegisterTsPiotAsync(client, token, options);
                return completion.IsSuccessful;
            },
            [DiagnosticFixKey.StartEsmServices] = (state, _, token) =>
                ExecuteServiceActionAsync(pointRepair, state.PointStatus.EsmServiceStatus ?? state.PointStatus.Esm, token),
            [DiagnosticFixKey.RestartEsm] = async (state, _, token) =>
            {
                await pointRepair.RestartServicesAsync(
                    state.PointStatus.EsmServiceStatus ?? state.PointStatus.Esm,
                    LicenseOperation.ManageServices,
                    token);
                await Task.Delay(TimeSpan.FromSeconds(3), token);
                return true;
            },
            [DiagnosticFixKey.StartKktServices] = (state, _, token) =>
                ExecuteServiceActionAsync(pointRepair, state.PointStatus.KktServiceStatus ?? state.PointStatus.Kkt, token),
            [DiagnosticFixKey.RestartLm] = async (state, _, token) =>
            {
                await pointRepair.RestartServicesAsync(state.PointStatus.Lm, LicenseOperation.RecoverLmServices, token);
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                return true;
            },
            [DiagnosticFixKey.RestartLmController] = async (_, _, token) =>
            {
                await pointRepair.RestartServiceAsync("esm-lm-controller", LicenseOperation.ManageServices, token);
                return true;
            },
            [DiagnosticFixKey.InitializeLm] = async (_, _, token) =>
                (await pointRepair.InitializeLmAsync(client.Token, null, token)).IsSuccess,
            [DiagnosticFixKey.RepairLmSync] = async (_, _, token) =>
            {
                var restore = new LmDatabaseRestoreService(
                    logService,
                    installationProgress,
                    dialogs,
                    createLicenseGuard(),
                    startup.UseRemoteConfigMode);
                return await restore.Restore(client, token);
            },
            [DiagnosticFixKey.RegisterTsPiot] = async (_, _, token) =>
                (await registration.RegisterAsync(token)).IsSuccess,
            [DiagnosticFixKey.ConfirmLmClientMismatch] = (_, _, token) =>
                installation.ReinstallAsync(client, new[] { InstallationComponent.LmModule }, token)
        };

        return new AutoFixWorkflow(
            new AutoFixPlanner(),
            new AutoFixExecutor(actions),
            async token =>
            {
                PointStatusRefreshResult refresh = await pointStatusRefresh.RefreshAsync(
                    client,
                    startup.RemoteVersions,
                    includeLicensedComponents: true,
                    token);
                await owner.Dispatcher.InvokeAsync(() => applyRefresh(refresh));
                return refresh;
            });
    }

    public static string ResultMessage(AutoFixResult result) => result.Status switch
    {
        AutoFixStatus.Success => "Автоматическое исправление завершено. Рабочее место проверено.",
        AutoFixStatus.NoFixNeeded => "Автоматическое исправление не требуется.",
        AutoFixStatus.RequiresUserAction => result.Message,
        AutoFixStatus.UnableToFix => result.Message,
        AutoFixStatus.Failed => "Не удалось выполнить исправление: " + result.Message,
        AutoFixStatus.Cancelled => "Автоматическое исправление отменено.",
        _ => result.Message
    };

    public static string[] HistoryLines(IEnumerable<AutoFixStepResult> history) =>
        (history ?? Array.Empty<AutoFixStepResult>()).Select(step =>
        {
            string marker = step.ExecutionStatus switch
            {
                AutoFixExecutionStatus.Success => "✓",
                AutoFixExecutionStatus.Cancelled => "—",
                AutoFixExecutionStatus.Unsupported => "!",
                _ => "×"
            };
            return $"{marker}  {step.Description}";
        }).ToArray();

    private static async Task<bool> ExecuteServiceActionAsync(
        PointRepairWorkflow workflow,
        NodeStatus status,
        CancellationToken cancellationToken)
    {
        ServiceActionPlan plan = workflow.CreateServiceActionPlan(status)
            ?? throw new InvalidOperationException("Не найдены службы для восстановления.");
        await workflow.ExecuteServiceActionAsync(plan, LicenseOperation.ManageServices, cancellationToken);
        return true;
    }

    private sealed class AutoFixDialogs : IUserDialogService
    {
        private readonly Window _owner;
        public AutoFixDialogs(Window owner) => _owner = owner;
        public void ShowInformation(string message, string title) { }
        public void ShowWarning(string message, string title) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        public void ShowError(string message, string title) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        public bool Confirm(string message, string title, UserDialogIcon icon = UserDialogIcon.Warning) =>
            Show(message, title, MessageBoxButton.YesNo,
                icon == UserDialogIcon.Error ? MessageBoxImage.Error : MessageBoxImage.Warning) == MessageBoxResult.Yes;
        private MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
            _owner.Dispatcher.Invoke(() => MessageBox.Show(_owner, message, title, buttons, image));
    }
}
