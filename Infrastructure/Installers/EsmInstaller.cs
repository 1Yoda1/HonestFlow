using HonestFlow.Infrastructure;
using System.IO;
using System.Threading.Tasks;

namespace ESM_Installer_SPI.Classes
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

        /// <summary>
        /// Установка ЕСМ
        /// </summary>
        public async Task<bool> InstallEsm()
        {
            if (!File.Exists(_esmPath))
            {
                Utils.Log("⚠️ Файл ЕСМ не найден", true);
                return false;
            }

            int code = await ProcessRunner.RunAsync(_esmPath, "/S", true);
            return code == 0;
        }

        /// <summary>
        /// Установка Контроллера
        /// </summary>
        public async Task<bool> InstallController()
        {
            if (!File.Exists(_controllerPath))
            {
                Utils.Log("⚠️ Файл Контроллера не найден", true);
                return false;
            }

            int code = await ProcessRunner.RunAsync(_controllerPath, "/S", true);
            return code == 0;
        }
    }
}