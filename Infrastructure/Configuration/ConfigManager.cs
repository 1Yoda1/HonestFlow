using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Downloads;
using HonestFlow.Models;

namespace HonestFlow.Infrastructure.Configuration
{
    /// <summary>
    /// Фасад совместимости для старого кода.
    /// Вся реальная работа разнесена по LocalConfigRepository, RemoteConfigRepository и GitHubDownloader.
    /// </summary>
    public static class ConfigManager
    {
        private static readonly LocalConfigRepository LocalConfig = new();
        private static readonly RemoteConfigRepository RemoteConfig = new();
        private static GitHubDownloader _downloader;

        public static List<IPData> LoadIps() => LocalConfig.LoadIps();
        public static void SaveIps(List<IPData> ips) => LocalConfig.SaveIps(ips);
        public static VersionsData LoadVersions() => LocalConfig.LoadVersions();
        public static void SaveVersions(VersionsData versions) => LocalConfig.SaveVersions(versions);

        public static (bool Success, List<IPData> Ips, VersionsData Versions) LoadConfigFromGitHub() => RemoteConfig.LoadAll();
        public static VersionsData LoadVersionsFromGitHub() => RemoteConfig.LoadVersions();
        public static List<IPData> LoadIpsFromGitHub() => RemoteConfig.LoadIps();

        public static void InitGitHubDownloader()
        {
            _downloader ??= new GitHubDownloader();
        }

        public static async Task<bool> DownloadInstallerIfNeeded(string fileName, IProgress<int> progress)
        {
            InitGitHubDownloader();

            if (_downloader.IsFileCached(fileName))
            {
                Logger.LogToFile($"✅ Файл уже в кэше: {fileName}");
                return true;
            }

            var assets = await _downloader.GetReleaseAssets();
            if (!assets.ContainsKey(fileName))
            {
                Logger.LogToFile($"❌ Файл не найден в релизе: {fileName}", true);
                return false;
            }

            string destination = Path.Combine(AppPaths.GitHubCacheFolder, fileName);
            return await _downloader.DownloadFileWithRetry(assets[fileName], destination, progress);
        }
        
        public static string GetInstallersFolder()
        {
            if (Directory.Exists(AppPaths.DistrFolder))
                return AppPaths.DistrFolder;

            if (Directory.Exists(AppPaths.GitHubCacheFolder) && Directory.EnumerateFiles(AppPaths.GitHubCacheFolder).Any())
                return AppPaths.GitHubCacheFolder;

            return AppPaths.BaseFolder;
        }
    }
}
