using HonestFlow.Application.Core;
using System;
using System.IO;
using System.Threading.Tasks;

namespace HonestFlow.Infrastructure.Installers
{
    public class AtolInstaller
    {
        private readonly string _installerPath;
        private readonly ILogService _log;
        private readonly bool _with1C;

        public AtolInstaller(string installerPath, ILogService logService, bool with1C = false)
        {
            _installerPath = installerPath;
            _log = logService ?? throw new ArgumentNullException(nameof(logService));
            _with1C = with1C;
        }

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

            string arguments = "/S /AcceptLicense /WithEOU";

            if (_with1C)
                arguments += " /With1C";

            _log.LogDebug($"Запуск установки АТОЛ: {_installerPath}");
            _log.LogDebug($"Аргументы АТОЛ: {arguments}");

            int code = await ProcessRunner.RunAsync(_installerPath, arguments, true);

            _log.LogDebug($"Установка АТОЛ завершена с кодом: {code}");

            return code == 0;
        }
    }
}