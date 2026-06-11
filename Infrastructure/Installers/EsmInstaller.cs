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

        public EsmInstaller(string esmPath, string controllerPath)
        {
            _esmPath = esmPath;
            _controllerPath = controllerPath;
        }

        public async Task<bool> InstallEsm()
        {
            if (string.IsNullOrEmpty(_esmPath))
            {
                Utils.Log("⚠️ Путь к ЕСМ не указан", true);
                return false;
            }

            if (!File.Exists(_esmPath))
            {
                Utils.Log($"⚠️ Файл ЕСМ не найден: {_esmPath}", true);
                return false;
            }

            int code = await ProcessRunner.RunAsync(_esmPath, "/S", true);
            return code == 0;
        }

        public async Task<bool> InstallController()
        {
            if (string.IsNullOrEmpty(_controllerPath))
            {
                Utils.Log("⚠️ Путь к Контроллеру не указан", true);
                return false;
            }

            if (!File.Exists(_controllerPath))
            {
                Utils.Log($"⚠️ Файл Контроллера не найден: {_controllerPath}", true);
                return false;
            }

            int code = await ProcessRunner.RunAsync(_controllerPath, "/S", true);
            return code == 0;
        }
    }
}