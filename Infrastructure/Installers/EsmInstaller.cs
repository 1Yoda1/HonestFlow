using HonestFlow.Services.Core;
using System;
using System.IO;
using System.Threading.Tasks;

namespace HonestFlow.Infrastructure.Installers
{
    /// <summary>
    /// Установщик ЕСМ и Контроллера.
    /// </summary>
    public class EsmInstaller
    {
        private readonly string _esmPath;
        private readonly string _controllerPath;
        private readonly ILogService _log;

        public EsmInstaller(string esmPath, string controllerPath, ILogService logService)
        {
            _esmPath = esmPath;
            _controllerPath = controllerPath;
            _log = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        public async Task<bool> InstallEsm()
        {
            if (string.IsNullOrWhiteSpace(_esmPath))
            {
                _log.LogUser("⚠️ Путь к ЕСМ не указан", true);
                return false;
            }

            if (!File.Exists(_esmPath))
            {
                _log.LogUser($"⚠️ Файл ЕСМ не найден: {_esmPath}", true);
                return false;
            }

            _log.LogDebug($"Запуск установки ЕСМ: {_esmPath}");
            int code = await ProcessRunner.RunAsync(_esmPath, "/S", true);
            _log.LogDebug($"Установка ЕСМ завершена с кодом: {code}");

            return code == 0;
        }

        public async Task<bool> InstallController()
        {
            if (string.IsNullOrWhiteSpace(_controllerPath))
            {
                _log.LogUser("⚠️ Путь к Контроллеру не указан", true);
                return false;
            }

            if (!File.Exists(_controllerPath))
            {
                _log.LogUser($"⚠️ Файл Контроллера не найден: {_controllerPath}", true);
                return false;
            }

            _log.LogDebug($"Запуск установки Контроллера: {_controllerPath}");
            int code = await ProcessRunner.RunAsync(_controllerPath, "/S", true);
            _log.LogDebug($"Установка Контроллера завершена с кодом: {code}");

            return code == 0;
        }
    }
}
