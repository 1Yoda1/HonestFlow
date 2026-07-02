using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using HonestFlow.Models;
using HonestFlow.Application.Core;

namespace HonestFlow.Application.Installation
{
    /// <summary>
    /// Сервис проверки установленных версий компонентов.
    /// Логика перенесена из старого static VersionChecker, чтобы сервис был настоящим сервисом.
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
            if (string.IsNullOrEmpty(expectedVersion))
                return false;

            string currentInfo = GetAtolDriverInfo();
            _log.LogDebug($"Текущий драйвер АТОЛ: {currentInfo}");

            if (currentInfo == "не установлен")
                return true;

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
            if (string.IsNullOrEmpty(expectedVersion))
                return false;

            string version = GetEsmVersion();
            _log.LogDebug($"Текущая версия ЕСМ: {version}");

            if (version == "не установлен")
                return true;

            return version != expectedVersion;
        }

        public bool NeedControllerInstall(string expectedVersion)
        {
            if (string.IsNullOrEmpty(expectedVersion))
                return false;

            string info = GetControllerVersion();
            _log.LogDebug($"Текущая версия Контроллера: {info}");

            if (info == "не установлен")
                return true;

            return info.Split(' ')[0] != expectedVersion;
        }

        public string GetAtolDriverInfo()
        {
            var foundVersions = new List<string>();
            string[] filePaths =
            {
                @"C:\Program Files (x86)\ATOL\Drivers10\KKT\bin\fptr10_t.exe",
                @"C:\Program Files\ATOL\Drivers10\KKT\bin\fptr10_t.exe"
            };

            foreach (var path in filePaths)
            {
                if (!File.Exists(path))
                    continue;

                var versionInfo = FileVersionInfo.GetVersionInfo(path);
                string arch = path.Contains("Program Files (x86)") ? "32-bit" : "64-bit";
                string version = versionInfo.FileVersion ?? "версия не определена";
                string entry = $"{version} ({arch})";

                if (!foundVersions.Contains(entry))
                    foundVersions.Add(entry);
            }

            if (foundVersions.Count == 0)
                return "не установлен";

            if (foundVersions.Count == 1)
                return foundVersions[0];

            return string.Join(" и ", foundVersions);
        }

        public string GetEsmVersion()
        {
            string[] possiblePaths =
            {
                @"C:\Program Files\ESP\ESM\Uninstall.exe",
                @"C:\Program Files (x86)\ESP\ESM\Uninstall.exe"
            };

            foreach (var path in possiblePaths)
            {
                if (!File.Exists(path))
                    continue;

                var versionInfo = FileVersionInfo.GetVersionInfo(path);
                return versionInfo.FileVersion ?? "версия не определена";
            }

            return "не установлен";
        }

        public string GetControllerVersion()
        {
            string[] possiblePaths =
            {
                @"C:\Program Files\ESP\LMController\Uninstall.exe",
                @"C:\Program Files (x86)\ESP\LMController\Uninstall.exe"
            };

            foreach (var path in possiblePaths)
            {
                if (!File.Exists(path))
                    continue;

                var versionInfo = FileVersionInfo.GetVersionInfo(path);
                string arch = path.Contains("Program Files (x86)") ? "32-bit" : "64-bit";
                return $"{versionInfo.FileVersion} ({arch})";
            }

            return "не установлен";
        }
    }
}
