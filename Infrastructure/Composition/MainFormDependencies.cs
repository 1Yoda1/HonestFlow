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
using HonestFlow.Models;

namespace HonestFlow.Infrastructure.Composition
{
    internal sealed class MainFormDependencies
    {
        public StartupResult Startup { get; init; }
        public ILogService LogService { get; init; }
        public IProgressService ProgressService { get; init; }
        public IUserDialogService DialogService { get; init; }
        public DiagnosticArchiveService DiagnosticArchiveService { get; init; }
        public DiagnosticsEmailSender DiagnosticsEmailSender { get; init; }
        public LmDatabaseRestoreService LmDatabaseRestoreService { get; init; }
        public LicensedLmInitializationService LmInitializationService { get; init; }
        public RuDesktopService RuDesktopService { get; init; }
        public IRuDesktopInstaller RuDesktopInstaller { get; init; }
        public HelpRequestDeliveryService HelpRequestDeliveryService { get; init; }
        public HelpRequestDataBuilder HelpRequestDataBuilder { get; init; }
        public AppRatingEmailSender AppRatingEmailSender { get; init; }
        public WindowsServiceControlService ServiceControlService { get; init; }
        public ComponentVersionStatusService ComponentVersionStatusService { get; init; }
        public PointStatusReportBuilder PointStatusReportBuilder { get; init; }
        public ExternalApplicationLauncher ExternalApplicationLauncher { get; init; }
        public WindowIconService WindowIconService { get; init; }
        public ILicenseObservationSnapshotStore LicenseSnapshotStore { get; init; }
        public ILicenseAccessPolicy LicenseAccessPolicy { get; init; }
        public ILicenseOperationGuard LicenseOperationGuard { get; init; }
        public DeviceRegistrationCoordinator DeviceRegistrationCoordinator { get; init; }
        public IPointAddressService PointAddressService { get; init; }
        public IInstallationService InstallationService { get; init; }
        public ComponentInstallationWorkflow ComponentInstallationWorkflow { get; init; }
        public IPointStatusService PointStatusService { get; init; }
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

            return new MainFormDependencies
            {
                Startup = startup,
                LogService = logService,
                ProgressService = progressService,
                DialogService = dialogService,
                DiagnosticArchiveService = new DiagnosticArchiveService(logService),
                DiagnosticsEmailSender = new DiagnosticsEmailSender(logService, ruDesktopService),
                LmDatabaseRestoreService = new LmDatabaseRestoreService(
                    logService,
                    progressService,
                    dialogService,
                    licenseOperationGuard,
                    useRemoteConfigMode),
                LmInitializationService = new LicensedLmInitializationService(licenseOperationGuard),
                RuDesktopService = ruDesktopService,
                RuDesktopInstaller = new RuDesktopInstaller(logService),
                HelpRequestDeliveryService = new HelpRequestDeliveryService(
                    helpRequestEmailSender,
                    new UnlicensedHelpRequestStore()),
                HelpRequestDataBuilder = new HelpRequestDataBuilder(),
                AppRatingEmailSender = new AppRatingEmailSender(logService),
                ServiceControlService = new WindowsServiceControlService(licenseOperationGuard),
                ComponentVersionStatusService = componentVersionStatusService,
                PointStatusReportBuilder = pointStatusReportBuilder,
                ExternalApplicationLauncher = new ExternalApplicationLauncher(),
                WindowIconService = new WindowIconService(logService),
                LicenseSnapshotStore = licenseSnapshotStore,
                LicenseAccessPolicy = licenseAccessPolicy,
                LicenseOperationGuard = licenseOperationGuard,
                DeviceRegistrationCoordinator = new DeviceRegistrationCoordinator(
                    new DeviceRegistrationRequestService(),
                    new SmtpDeviceRegistrationRequestSender(),
                    new DpapiDeviceRegistrationDeliveryStateStore()),
                PointAddressService = new PointAddressService(logService),
                InstallationService = installationService,
                ComponentInstallationWorkflow = new ComponentInstallationWorkflow(installationService),
                PointStatusService = pointStatusService,
                PointStatusRefreshService = new PointStatusRefreshService(
                    pointStatusService,
                    componentVersionStatusService,
                    pointStatusReportBuilder)
            };
        }
    }
}
