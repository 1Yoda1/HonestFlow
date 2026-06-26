using System;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Services.Auth;
using HonestFlow.Services.Core;
using HonestFlow.Services.Installation;

namespace HonestFlow.Application.Bootstrap
{
    /// <summary>
    /// Создаёт режим запуска приложения: Yandex Disk-конфиги или локальные файлы.
    /// Форма больше не должна знать, как именно подтягиваются конфиги и создаются сервисы.
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
                var result = ConfigManager.LoadConfigFromYandexDisk();
                if (result.Success && result.Ips != null && result.Ips.Count > 0)
                {
                    ConfigManager.InitGitHubDownloader();
                    Logger.LogToFile("Remote mode: configs loaded from Yandex Disk");

                    return new StartupResult
                    {
                        UseGitHubMode = true,
                        GitHubIps = result.Ips,
                        GitHubVersions = result.Versions,
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
                Logger.LogToFile($"⚠️ Режим v1.2.3: используем локальные файлы. Ошибка: {ex.Message}");

                return new StartupResult
                {
                    UseGitHubMode = false,
                    AuthService = authService,
                    InstallationService = new InstallationService(_logService, _progressService, _dialogService, false)
                };
            }
        }
    }
}
