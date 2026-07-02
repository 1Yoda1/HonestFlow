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
                var result = ConfigManager.LoadRemoteConfig();
                if (result.Success && result.Ips != null && result.Ips.Count > 0)
                {
                    ConfigManager.InitYandexDiskDownloader();
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
                var authService = new AuthService(_logService);
                authService.LoadIpList();
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
