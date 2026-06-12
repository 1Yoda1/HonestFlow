using HonestFlow.Services.Core;
using System.IO;
using System.Threading.Tasks;

namespace HonestFlow.Infrastructure.Installers
{
    /// <summary>
    /// Установщик ЕСМ и Контроллера
    /// </summary>
    public class EsmInstaller
    {
        private readonly string _esmPath;
        private readonly string _controllerPath;
        private readonly ILogService _log;

        public EsmInstaller(string esmPath, string controllerPath, ILogService log)
        {
            _esmPath = esmPath;
            _controllerPath = controllerPath;
            _log = log;
        }

        public async Task<bool> InstallEsm()
        {
            if (string.IsNullOrEmpty(_esmPath))
            {
                _log.LogUser("⚠️ Путь к ЕСМ не указан", true);
                return false;
            }

            if (!File.Exists(_esmPath))
            {
                _log.LogUser($"⚠️ Файл ЕСМ не найден: {_esmPath}", true);
                return false;
            }

            int code = await ProcessRunner.RunAsync(_esmPath, "/S", true);
            return code == 0;
        }

        public async Task<bool> InstallController()
        {
            if (string.IsNullOrEmpty(_controllerPath))
            {
                _log.LogUser("⚠️ Путь к Контроллеру не указан", true);
                return false;
            }

            if (!File.Exists(_controllerPath))
            {
                _log.LogUser($"⚠️ Файл Контроллера не найден: {_controllerPath}", true);
                return false;
            }

            int code = await ProcessRunner.RunAsync(_controllerPath, "/S", true);
            return code == 0;
        }
    }
}