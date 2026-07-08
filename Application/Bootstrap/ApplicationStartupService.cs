using System;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Core;
using HonestFlow.Application.Installation;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Models;

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
            try
            {
                _progressService.SetProgress(35, "\u041f\u0440\u043e\u0431\u0443\u0435\u043c \u043f\u043e\u043b\u0443\u0447\u0438\u0442\u044c \u043a\u043e\u043d\u0444\u0438\u0433\u0438 \u0438\u0437 \u043e\u0431\u043b\u0430\u043a\u0430...");
                var result = ConfigManager.LoadRemoteConfig();
                if (result.Success && result.Ips != null && result.Ips.Count > 0)
                {
                    _progressService.SetProgress(70, "\u0421\u043f\u0438\u0441\u043a\u0438 \u0442\u043e\u0447\u0435\u043a \u0438 \u0432\u0435\u0440\u0441\u0438\u0438 \u0437\u0430\u0433\u0440\u0443\u0436\u0435\u043d\u044b");
                    ConfigManager.InitYandexDiskDownloader();
                    _progressService.SetProgress(82, "\u0413\u043e\u0442\u043e\u0432\u0438\u043c \u0437\u0430\u0433\u0440\u0443\u0437\u0447\u0438\u043a \u0434\u0438\u0441\u0442\u0440\u0438\u0431\u0443\u0442\u0438\u0432\u043e\u0432...");
                    Logger.LogToFile("Remote mode: configs loaded from Yandex Disk");

                    return new StartupResult
                    {
                        UseRemoteConfigMode = true,
                        Ips = result.Ips,
                        RemoteIps = result.Ips,
                        RemoteVersions = result.Versions,
                        AuthService = new AuthService(result.Ips, _logService),
                        InstallationService = new InstallationService(_logService, _progressService, _dialogService, true)
                    };
                }

                throw new Exception("Yandex Disk did not respond or returned empty data");
            }
            catch (Exception ex)
            {
                _progressService.SetProgress(58, "\u041e\u0431\u043b\u0430\u043a\u043e \u043d\u0435 \u043e\u0442\u0432\u0435\u0442\u0438\u043b\u043e, \u0431\u0435\u0440\u0435\u043c \u043b\u043e\u043a\u0430\u043b\u044c\u043d\u044b\u0435 \u0441\u043f\u0438\u0441\u043a\u0438...");
                var authService = new AuthService(_logService);
                authService.LoadIpList();
                _progressService.SetProgress(78, "\u041b\u043e\u043a\u0430\u043b\u044c\u043d\u044b\u0435 \u0441\u043f\u0438\u0441\u043a\u0438 \u0437\u0430\u0433\u0440\u0443\u0436\u0435\u043d\u044b");
                Logger.LogToFile($"Remote config unavailable, using local files. Error: {ex.Message}");

                return new StartupResult
                {
                    UseRemoteConfigMode = false,
                    Ips = new System.Collections.Generic.List<IPData>(authService.Ips),
                    AuthService = authService,
                    InstallationService = new InstallationService(_logService, _progressService, _dialogService, false)
                };
            }
        }
    }
}
