using System;
using System.Collections.Generic;
using System.Windows.Forms;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Bootstrap;
using HonestFlow.Application.Core;
using HonestFlow.Application.Diagnostics;
using HonestFlow.Application.Feedback;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.Lm;
using HonestFlow.Application.PointIdentity;
using HonestFlow.Application.PointStatus;
using HonestFlow.Application.RemoteAccess;
using HonestFlow.Application.Ui;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Infrastructure.Licensing;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Models;

namespace HonestFlow.Infrastructure.Composition
{
    internal sealed class MainFormDependencies
    {
        public StartupResult Startup { get; init; }
        public ILogService LogService { get; init; }
        public MainFormDialogService MainFormDialogService { get; init; }
        public DiagnosticWorkflowService DiagnosticWorkflowService { get; init; }
        public RuDesktopWorkflow RuDesktopWorkflow { get; init; }
        public HelpRequestWorkflow HelpRequestWorkflow { get; init; }
        public AppRatingEmailSender AppRatingEmailSender { get; init; }
        public PointRepairWorkflow PointRepairWorkflow { get; init; }
        public PointStatusReportBuilder PointStatusReportBuilder { get; init; }
        public ExternalApplicationLauncher ExternalApplicationLauncher { get; init; }
        public WindowIconService WindowIconService { get; init; }
        public ILicenseObservationSnapshotStore LicenseSnapshotStore { get; init; }
        public ILicenseAccessPolicy LicenseAccessPolicy { get; init; }
        public LicensePresentationService LicensePresentationService { get; init; }
        public LicenseRefreshWorkflow LicenseRefreshWorkflow { get; init; }
        public SellerAuthenticationWorkflow SellerAuthenticationWorkflow { get; init; }
        public DeviceRegistrationWorkflow DeviceRegistrationWorkflow { get; init; }
        public ComponentInstallationWorkflow ComponentInstallationWorkflow { get; init; }
        public MaintenanceWorkflow MaintenanceWorkflow { get; init; }
        public PointStatusRefreshService PointStatusRefreshService { get; init; }
    }

    internal static class MainFormCompositionRoot
    {
        public static MainFormDependencies Create(
            Form owner,
            ProgressBar progressBar,
            Label statusLabel,
            StartupResult startup,
            Func<string> selectedClientId)
        {
            var logService = new LogService();
            var progressService = new ProgressService(progressBar, statusLabel);
            var dialogService = new WinFormsDialogService(owner);
            startup ??= new ApplicationStartupService(logService, progressService, dialogService).Start();

            bool useRemoteConfigMode = startup.UseRemoteConfigMode;
            List<IPData> remoteIps = startup.Ips ?? startup.RemoteIps;
            var ruDesktopService = new RuDesktopService(logService);
            var licenseSnapshotStore = LicenseObservationSnapshotStore.Instance;
            var licenseAccessPolicy = new LicenseAccessPolicy(
                LicenseRuntimeConfiguration.FromEnvironment().EnforcementMode,
                licenseSnapshotStore,
                selectedClientId);
            var licenseOperationGuard = new LicenseOperationGuard(licenseAccessPolicy);
            var helpRequestEmailSender = new HelpRequestEmailSender(logService);

            var componentVersionStatusService = new ComponentVersionStatusService(logService);
            var pointStatusReportBuilder = new PointStatusReportBuilder();
            var pointStatusService = new PointStatusService(
                useRemoteConfigMode,
                remoteIps?.Count ?? 0,
                remoteIps,
                ruDesktopService);

            var installationService = new InstallationService(
                logService,
                progressService,
                dialogService,
                licenseOperationGuard,
                useRemoteConfigMode);

            var pointAddressService = new PointAddressService(logService);

            var diagnosticArchiveService = new DiagnosticArchiveService(logService);
            var diagnosticsEmailSender = new DiagnosticsEmailSender(logService, ruDesktopService);

            var componentInstallationWorkflow = new ComponentInstallationWorkflow(installationService);
            var lmDatabaseRestoreService = new LmDatabaseRestoreService(
                logService,
                progressService,
                dialogService,
                licenseOperationGuard,
                useRemoteConfigMode);

            var lmInitializationService = new LicensedLmInitializationService(licenseOperationGuard);
            var serviceControlService = new WindowsServiceControlService(licenseOperationGuard);

            var ruDesktopInstaller = new RuDesktopInstaller(logService);

            var helpRequestDataBuilder = new HelpRequestDataBuilder();
            var helpRequestDeliveryService = new HelpRequestDeliveryService(
                helpRequestEmailSender,
                new UnlicensedHelpRequestStore());

            IDeviceRegistrationRequestSender registrationSender = startup.AuthService is IApiSessionProvider apiProvider &&
                apiProvider.ApiSessionService != null
                ? new ApiDeviceRegistrationRequestSender(apiProvider.ApiSessionService)
                : new SmtpDeviceRegistrationRequestSender();
            var deviceRegistrationCoordinator = new DeviceRegistrationCoordinator(
                new DeviceRegistrationRequestService(),
                registrationSender,
                new DpapiDeviceRegistrationDeliveryStateStore());

            return new MainFormDependencies
            {
                Startup = startup,
                LogService = logService,
                MainFormDialogService = new MainFormDialogService(
                    owner,
                    pointAddressService,
                    licenseSnapshotStore),
                DiagnosticWorkflowService = new DiagnosticWorkflowService(
                    diagnosticArchiveService,
                    diagnosticsEmailSender),
                RuDesktopWorkflow = new RuDesktopWorkflow(
                    ruDesktopService,
                    ruDesktopInstaller,
                    logService),
                HelpRequestWorkflow = new HelpRequestWorkflow(
                    pointStatusService,
                    helpRequestDataBuilder,
                    helpRequestDeliveryService),
                AppRatingEmailSender = new AppRatingEmailSender(logService),
                PointRepairWorkflow = new PointRepairWorkflow(
                    serviceControlService,
                    lmInitializationService),
                PointStatusReportBuilder = pointStatusReportBuilder,
                ExternalApplicationLauncher = new ExternalApplicationLauncher(),
                WindowIconService = new WindowIconService(logService),
                LicenseSnapshotStore = licenseSnapshotStore,
                LicenseAccessPolicy = licenseAccessPolicy,
                LicensePresentationService = new LicensePresentationService(),
                LicenseRefreshWorkflow = new LicenseRefreshWorkflow(startup.AuthService, logService),
                SellerAuthenticationWorkflow = new SellerAuthenticationWorkflow(
                    startup.AuthService,
                    licenseSnapshotStore),
                DeviceRegistrationWorkflow = new DeviceRegistrationWorkflow(deviceRegistrationCoordinator),
                ComponentInstallationWorkflow = componentInstallationWorkflow,
                MaintenanceWorkflow = new MaintenanceWorkflow(
                    componentInstallationWorkflow,
                    lmDatabaseRestoreService),
                PointStatusRefreshService = new PointStatusRefreshService(
                    pointStatusService,
                    componentVersionStatusService,
                    pointStatusReportBuilder)
            };
        }
    }
}
