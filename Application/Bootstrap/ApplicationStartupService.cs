using System;
using System.IO;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Core;
using HonestFlow.Application.Installation;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Models;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Infrastructure.DeviceIdentity;
using System.Net.Http;

namespace HonestFlow.Application.Bootstrap
{
    /// <summary>
    /// Builds the application startup mode: remote Yandex Disk configuration or local files.
    /// </summary>
    public class ApplicationStartupService
    {
        private readonly ILogService _logService;
        private readonly IProgressService _progressService;
        private readonly IUserDialogService _dialogService;

        public ApplicationStartupService(ILogService logService, IProgressService progressService, IUserDialogService dialogService)
        {
            _logService = logService;
            _progressService = progressService;
            _dialogService = dialogService;
        }

        public StartupResult Start()
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable("HONESTFLOW_USE_LEGACY_STARTUP"),
                    "1",
                    StringComparison.Ordinal))
            {
                return StartApiFirst();
            }

            try
            {
                _progressService.SetProgress(35, "\u041f\u0440\u043e\u0431\u0443\u0435\u043c \u043f\u043e\u043b\u0443\u0447\u0438\u0442\u044c \u043a\u043e\u043d\u0444\u0438\u0433\u0438 \u0438\u0437 \u043e\u0431\u043b\u0430\u043a\u0430...");
                var result = ConfigManager.LoadRemoteConfig();
                if (result.Success && result.Ips != null && result.Ips.Count > 0)
                {
                    new VersionConfigurationCache().Save(result.Versions);
                    RemoveLegacyFullClientList();
                    _progressService.SetProgress(70, "\u0421\u043f\u0438\u0441\u043a\u0438 \u0442\u043e\u0447\u0435\u043a \u0438 \u0432\u0435\u0440\u0441\u0438\u0438 \u0437\u0430\u0433\u0440\u0443\u0436\u0435\u043d\u044b");
                    ConfigManager.InitYandexDiskDownloader();
                    _progressService.SetProgress(82, "\u0413\u043e\u0442\u043e\u0432\u0438\u043c \u0437\u0430\u0433\u0440\u0443\u0437\u0447\u0438\u043a \u0434\u0438\u0441\u0442\u0440\u0438\u0431\u0443\u0442\u0438\u0432\u043e\u0432...");
                    Logger.Info(
                        $"Удалённая конфигурация загружена с Яндекс Диска: {result.Ips.Count} точек",
                        nameof(ApplicationStartupService));

                    return new StartupResult
                    {
                        UseRemoteConfigMode = true,
                        Ips = result.Ips,
                        RemoteIps = result.Ips,
                        RemoteVersions = result.Versions,
                        AuthService = new AuthService(result.Ips, _logService)
                    };
                }

                throw new Exception("Yandex Disk did not respond or returned empty data");
            }
            catch (Exception ex)
            {
                _progressService.SetProgress(58, "\u041e\u0431\u043b\u0430\u043a\u043e \u043d\u0435 \u043e\u0442\u0432\u0435\u0442\u0438\u043b\u043e, \u0431\u0435\u0440\u0435\u043c \u043b\u043e\u043a\u0430\u043b\u044c\u043d\u044b\u0435 \u0441\u043f\u0438\u0441\u043a\u0438...");
                IPData cachedClient = new AuthorizedClientCache().Load();
                VersionsData cachedVersions = new VersionConfigurationCache().Load();
                var offlineClients = cachedClient == null
                    ? new System.Collections.Generic.List<IPData>()
                    : new System.Collections.Generic.List<IPData> { cachedClient };
                var authService = new AuthService(offlineClients, _logService);
                _progressService.SetProgress(78, "\u041b\u043e\u043a\u0430\u043b\u044c\u043d\u044b\u0435 \u0441\u043f\u0438\u0441\u043a\u0438 \u0437\u0430\u0433\u0440\u0443\u0436\u0435\u043d\u044b");
                Logger.LogToFile(
                    $"Remote config unavailable, using single-client protected cache. " +
                    $"CachedClientAvailable={cachedClient != null}. " +
                    $"CachedVersionsAvailable={cachedVersions != null}. Error: {ex.Message}");

                return new StartupResult
                {
                    UseRemoteConfigMode = false,
                    Ips = new System.Collections.Generic.List<IPData>(authService.Ips),
                    RemoteVersions = cachedVersions,
                    AuthService = authService
                };
            }
        }

        private StartupResult StartApiFirst()
        {
            _progressService.SetProgress(58, "Подготавливаем безопасное подключение к HonestLicenseServer...");
            string configuredBaseUrl = Environment.GetEnvironmentVariable("HONESTFLOW_API_BASE_URL");
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(string.IsNullOrWhiteSpace(configuredBaseUrl)
                    ? "https://api.honestflow.ru/"
                    : configuredBaseUrl.TrimEnd('/') + "/"),
                Timeout = System.Threading.Timeout.InfiniteTimeSpan
            };
            var session = new ApiSessionService(
                httpClient,
                new FileApiSessionStore(),
                new FileApiSessionStore(Path.Combine(AppPaths.ProgramDataFolder,
                    "registration-continuation.dpapi")));
            var auth = new ApiAuthService(
                session,
                new FileDeviceIdentityService(new DpapiDeviceIdentityStateProtector()),
                _logService,
                new FileApiConfigurationCache());

            // Yandex Disk remains available only to the installer download infrastructure.
            ConfigManager.InitYandexDiskDownloader();
            _progressService.SetProgress(78, "Введите логин и пароль HonestFlow.");
            Logger.Info("Event=ApiFirstStartupConfigured Source=HonestLicenseServer", nameof(ApplicationStartupService));
            return new StartupResult
            {
                UseRemoteConfigMode = true,
                Ips = new System.Collections.Generic.List<IPData>(),
                RemoteIps = new System.Collections.Generic.List<IPData>(),
                AuthService = auth
            };
        }

        private static void RemoveLegacyFullClientList()
        {
            try
            {
                if (!File.Exists(AppPaths.LocalIpsFile))
                    return;

                File.Delete(AppPaths.LocalIpsFile);
                Logger.Info("Event=LegacyFullClientListRemoved", nameof(ApplicationStartupService));
            }
            catch (Exception ex)
            {
                Logger.Warning(
                    $"Event=LegacyFullClientListRemoveFailed ErrorType={ex.GetType().Name}",
                    nameof(ApplicationStartupService));
            }
        }
    }
}
