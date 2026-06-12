using HonestFlow.Services.Core;
using System;
using System.IO;
using System.Threading.Tasks;

namespace HonestFlow.Infrastructure.Installers
{
    /// <summary>
    /// Установщик драйвера АТОЛ.
    /// </summary>
    public class AtolInstaller
    {
        private readonly string _installerPath;
        private readonly ILogService _log;

        public AtolInstaller(string installerPath, ILogService logService)
        {
            _installerPath = installerPath;
            _log = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        /// <summary>
        /// Запуск установки драйвера АТОЛ в тихом режиме.
        /// </summary>
        public async Task<bool> Install()
        {
            if (string.IsNullOrWhiteSpace(_installerPath))
            {
                _log.LogUser("⚠️ Путь к драйверу АТОЛ не указан", true);
                return false;
            }

            if (!File.Exists(_installerPath))
            {
                _log.LogUser($"⚠️ Файл драйвера АТОЛ не найден: {_installerPath}", true);
                return false;
            }

            _log.LogDebug($"Запуск установки АТОЛ: {_installerPath}");
            int code = await ProcessRunner.RunAsync(_installerPath, "/S /AcceptLicense", true);
            _log.LogDebug($"Установка АТОЛ завершена с кодом: {code}");

            return code == 0;
        }
    }
}
