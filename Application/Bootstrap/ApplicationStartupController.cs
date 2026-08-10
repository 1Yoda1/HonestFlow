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
using HonestFlow.Infrastructure.Api;

namespace HonestFlow.Application.Bootstrap
{
    public sealed class ApplicationStartupController
    {
        private ApplicationStartupSession _activeSession;

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
            _activeSession = new ApplicationStartupSession(startup, logService, new SellerAuthenticationWorkflow(startup.AuthService, LicenseObservationSnapshotStore.Instance));
            return _activeSession;
        }

        public async Task<LicenseAuthenticationResult> AuthenticateAsync(ApplicationStartupSession session, string login, string password, bool remember, IProgress<LicenseAuthenticationProgress> progress, CancellationToken cancellationToken)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            LicenseAuthenticationResult result = await session.Authentication.AuthenticateAsync(login, password, progress, cancellationToken);
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

        public Task<LicenseAuthenticationResult> AuthenticateAsync(ApplicationStartupSession session, string password, bool remember, IProgress<LicenseAuthenticationProgress> progress, CancellationToken cancellationToken) =>
            AuthenticateAsync(session, string.Empty, password, remember, progress, cancellationToken);

        public async Task<LicenseAuthenticationResult> TryResumeAsync(
            ApplicationStartupSession session,
            IProgress<LicenseAuthenticationProgress> progress,
            CancellationToken cancellationToken)
        {
            if (session?.Startup.AuthService is not IApiCredentialAuthService apiAuth)
                return new LicenseAuthenticationResult(null, null);
            LicenseAuthenticationResult result = await apiAuth.TryResumeAsync(progress, cancellationToken);
            if (result.Client != null)
            {
                session.Startup.AuthorizedClient = result.Client;
                session.Startup.SellerAuthenticationHandled = true;
            }
            return result;
        }

        public async Task LogoutAsync(ApplicationStartupSession session, CancellationToken cancellationToken)
        {
            if (session?.Startup.AuthService is IApiSessionProvider provider && provider.ApiSessionService != null)
                await provider.ApiSessionService.LogoutAsync(cancellationToken);
            await new FileApiConfigurationCache().ClearAsync(CancellationToken.None);
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
            IDeviceRegistrationRequestSender sender = _activeSession?.Startup.AuthService is IApiSessionProvider provider &&
                provider.ApiSessionService != null
                ? new ApiDeviceRegistrationRequestSender(provider.ApiSessionService)
                : new SmtpDeviceRegistrationRequestSender();
            var workflow = new DeviceRegistrationWorkflow(new DeviceRegistrationCoordinator(new DeviceRegistrationRequestService(), sender, new DpapiDeviceRegistrationDeliveryStateStore()));
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
