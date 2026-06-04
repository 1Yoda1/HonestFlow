using HonestFlow.Infrastructure;
using HonestFlow.Models;
using HonestFlow.Services.Core;

namespace HonestFlow.Services.Installation
{
    /// <summary>
    /// Реализация сервиса проверки версий компонентов
    /// </summary>
    public class VersionCheckService : IVersionCheckService
    {
        private readonly ILogService _log;

        public VersionCheckService(ILogService logService)
        {
            _log = logService;
        }

        public bool NeedAtolInstall(IPData selectedIP, string expectedVersion)
        {
            if (string.IsNullOrEmpty(expectedVersion)) return false;

            string currentInfo = VersionChecker.GetAtolDriverInfo();
            _log.LogDebug($"Текущий драйвер АТОЛ: {currentInfo}");

            if (currentInfo == "не установлен") return true;

            string currentVersion = currentInfo.Split(' ')[0];
            string needArch = selectedIP.Architecture;
            bool hasCorrectArch = false;

            if (needArch == "x64" && currentInfo.Contains("64-bit"))
                hasCorrectArch = true;
            else if (needArch == "x86" && currentInfo.Contains("32-bit"))
                hasCorrectArch = true;

            bool needInstall = !hasCorrectArch || currentVersion != expectedVersion;

            if (!hasCorrectArch)
                _log.LogDebug($"⚠️ Нужная разрядность {needArch} не найдена среди установленных");
            if (currentVersion != expectedVersion)
                _log.LogDebug($"⚠️ Версия не совпадает: {currentVersion} != {expectedVersion}");

            return needInstall;
        }

        public bool NeedEsmInstall(string expectedVersion)
        {
            if (string.IsNullOrEmpty(expectedVersion)) return false;
            string version = VersionChecker.GetEsmVersion();
            _log.LogDebug($"Текущая версия ЕСМ: {version}");
            if (version == "не установлен") return true;
            return version != expectedVersion;
        }

        public bool NeedControllerInstall(string expectedVersion)
        {
            if (string.IsNullOrEmpty(expectedVersion)) return false;
            string info = VersionChecker.GetControllerVersion();
            _log.LogDebug($"Текущая версия Контроллера: {info}");
            if (info == "не установлен") return true;
            return info.Split(' ')[0] != expectedVersion;
        }
    }
}