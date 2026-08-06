using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Core;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.RemoteAccess;
using HonestFlow.Application.PointStatus;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Infrastructure.DeviceIdentity;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Infrastructure.Licensing;

namespace HonestFlow.Application.Bootstrap
{
    public sealed class ApplicationStartupController
    {
        public async Task<ApplicationStartupSession> InitializeAsync(IProgressService progress, IUserDialogService dialogs, CancellationToken cancellationToken)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            if (dialogs == null) throw new ArgumentNullException(nameof(dialogs));
            Logger.Initialize();
            progress.SetProgress(12, "Подготавливаем служебные каталоги...");
            new InstallerCacheLocationStore().RegisterLocations(new[] { AppPaths.LegacyYandexDiskCacheFolder, AppPaths.LegacyRemoteCacheFolder });
            progress.SetProgress(24, "Получаем идентификатор компьютера...");
            await new FileDeviceIdentityService(new DpapiDeviceIdentityStateProtector()).GetOrCreateAsync(cancellationToken);
            var logService = new LogService();
            progress.SetProgress(28, "Загружаем конфигурацию точки...");
            StartupResult startup = await Task.Run(() => new ApplicationStartupService(logService, progress, dialogs).Start(), cancellationToken);
            startup.AuthService = LicenseObservationBootstrap.WrapAuthService(startup.AuthService);
            return new ApplicationStartupSession(startup, logService, new SellerAuthenticationWorkflow(startup.AuthService, LicenseObservationSnapshotStore.Instance));
        }

        public async Task<LicenseAuthenticationResult> AuthenticateAsync(ApplicationStartupSession session, string password, bool remember, IProgress<LicenseAuthenticationProgress> progress, CancellationToken cancellationToken)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            LicenseAuthenticationResult result = await session.Authentication.AuthenticateAsync(password, progress, cancellationToken);
            if (result.Client == null) return result;
            if (remember)
            {
                if (session.Startup.UseRemoteConfigMode)
                {
                    try
                    {
                        new AuthorizedClientCache().Save(result.Client);
                    }
                    catch (Exception ex) when (ex is System.IO.IOException ||
                                               ex is UnauthorizedAccessException ||
                                               ex is System.Security.Cryptography.CryptographicException)
                    {
                        Logger.Warning(
                            $"Event=AuthorizedClientCacheWriteFailed ErrorType={ex.GetType().Name}",
                            nameof(ApplicationStartupController));
                    }
                }
                try
                {
                    new RuDesktopService(session.LogService).SaveLastAuthorizedClient(result.Client);
                }
                catch (Exception ex) when (ex is System.IO.IOException ||
                                           ex is UnauthorizedAccessException ||
                                           ex is System.Security.SecurityException)
                {
                    Logger.Warning(
                        $"Event=RuDesktopAuthorizedClientWriteFailed ErrorType={ex.GetType().Name}",
                        nameof(ApplicationStartupController));
                }
            }
            session.Startup.AuthorizedClient = result.Client;
            session.Startup.SellerAuthenticationHandled = true;
            return result;
        }

        public async Task SendHelpRequestAsync(
            ApplicationStartupSession session,
            LicenseObservationSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            var ruDesktop = new RuDesktopService(session.LogService);
            var pointStatus = new PointStatusService(
                session.Startup.UseRemoteConfigMode,
                session.Startup.Ips?.Count ?? session.Startup.RemoteIps?.Count ?? 0,
                session.Startup.Ips ?? session.Startup.RemoteIps,
                ruDesktop);
            var workflow = new HelpRequestWorkflow(
                pointStatus,
                new HelpRequestDataBuilder(),
                new HelpRequestDeliveryService(
                    new HelpRequestEmailSender(session.LogService),
                    new UnlicensedHelpRequestStore()));
            await workflow.SendAsync(new HelpRequestWorkflowInput
            {
                SelectedClient = session.Startup.AuthorizedClient,
                LastClient = ruDesktop.GetLastAuthorizedClient(),
                RuDesktopId = ruDesktop.GetLastKnownId(),
                HonestFlowVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                ProblemType = "Регистрация устройства / доступ к HonestFlow",
                Message = "Автоматический запрос помощи из стартового окна WPF.",
                LicenseSnapshot = snapshot
            }, cancellationToken);
        }
        public Task<DeviceRegistrationDeliveryStatus> RegisterDeviceAsync(LicenseObservationSnapshot snapshot, CancellationToken cancellationToken)
        {
            var workflow = new DeviceRegistrationWorkflow(new DeviceRegistrationCoordinator(new DeviceRegistrationRequestService(), new SmtpDeviceRegistrationRequestSender(), new DpapiDeviceRegistrationDeliveryStateStore()));
            return workflow.SendAsync(snapshot, snapshot?.PointAddress, Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown", cancellationToken);
        }
    }

    public sealed class ApplicationStartupSession
    {
        public ApplicationStartupSession(StartupResult startup, ILogService logService, SellerAuthenticationWorkflow authentication)
        {
            Startup = startup ?? throw new ArgumentNullException(nameof(startup));
            LogService = logService ?? throw new ArgumentNullException(nameof(logService));
            Authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        }
        public StartupResult Startup { get; }
        public ILogService LogService { get; }
        public SellerAuthenticationWorkflow Authentication { get; }
    }
}
