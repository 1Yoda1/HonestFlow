using System;
using HonestFlow.Infrastructure;
using HonestFlow.Services.Auth;
using HonestFlow.Services.Core;
using HonestFlow.Services.Installation;

namespace HonestFlow.Application.Bootstrap
{
    /// <summary>
    /// Создаёт режим запуска приложения: GitHub-конфиги или локальные файлы.
    /// Форма больше не должна знать, как именно подтягиваются конфиги и создаются сервисы.
    /// </summary>
    public class ApplicationStartupService
    {
        private readonly ILogService _logService;
        private readonly IProgressService _progressService;

        public ApplicationStartupService(ILogService logService, IProgressService progressService)
        {
            _logService = logService;
            _progressService = progressService;
        }

        public StartupResult Start()
        {
            try
            {
                var result = ConfigManager.LoadConfigFromGitHub();
                if (result.Success && result.Ips != null && result.Ips.Count > 0)
                {
                    ConfigManager.InitGitHubDownloader();
                    Logger.LogToFile("✅ Режим v1.3: конфиги загружены с GitHub");

                    return new StartupResult
                    {
                        UseGitHubMode = true,
                        GitHubIps = result.Ips,
                        GitHubVersions = result.Versions,
                        AuthService = new AuthService(result.Ips, _logService),
                        InstallationService = new InstallationService(_logService, _progressService, true)
                    };
                }

                throw new Exception("GitHub не ответил или данные пусты");
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
                    InstallationService = new InstallationService(_logService, _progressService, false)
                };
            }
        }
    }
}
