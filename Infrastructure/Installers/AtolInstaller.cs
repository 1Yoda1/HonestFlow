using HonestFlow.Services.Core;
using System.IO;
using System.Threading.Tasks;


namespace HonestFlow.Infrastructure.Installers
{
    /// <summary>
    /// Установщик драйвера АТОЛ
    /// </summary>

    public class AtolInstaller
    {
        private readonly ILogService _log;

        public AtolInstaller(ILogService log)
        {
            _log = log;
        }

        private readonly string _installerPath;

        public AtolInstaller(string installerPath) => _installerPath = installerPath;

        /// <summary>
        /// Запуск установки драйвера АТОЛ в тихом режиме
        /// </summary>
        public async Task<bool> Install()
        {
            if (!File.Exists(_installerPath))
            {
                _log.LogUser("⚠️ Файл драйвера АТОЛ не найден", true);
                return false;
            }

            int code = await ProcessRunner.RunAsync(_installerPath, "/S /AcceptLicense", true);
            return code == 0;
        }
    }
}