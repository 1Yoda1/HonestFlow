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
    /// Фасад совместимости для конфигурации и загрузки установщиков.
    /// </summary>
    public static class ConfigManager
    {
        private static readonly LocalConfigRepository LocalConfig = new();
        private static readonly RemoteConfigRepository RemoteConfig = new();
        private static GitHubDownloader _downloader;

        public static List<IPData> LoadIps() => LocalConfig.LoadIps();
        public static VersionsData LoadVersions() => LocalConfig.LoadVersions();
        public static (bool Success, List<IPData> Ips, VersionsData Versions) LoadConfigFromGitHub() => RemoteConfig.LoadAll();
        public static VersionsData LoadVersionsFromGitHub() => RemoteConfig.LoadVersions();

        public static void InitGitHubDownloader()
        {
            _downloader ??= new GitHubDownloader();
        }

        public static async Task<bool> DownloadInstallerIfNeeded(string fileName, IProgress<int> progress)
        {
            InitGitHubDownloader();

            var assets = await _downloader.GetReleaseAssets();
            if (!assets.TryGetValue(fileName, out var asset))
            {
                Logger.LogToFile($"Файл не найден в релизе: {fileName}", true);
                return false;
            }

            string destination = Path.Combine(AppPaths.GitHubCacheFolder, fileName);

            if (_downloader.IsFileCached(fileName, asset.Size))
            {
                Logger.LogToFile($"Файл уже в кэше: {fileName}, размер: {asset.Size} байт");
                return true;
            }

            if (File.Exists(destination))
            {
                long actualBytes = new FileInfo(destination).Length;
                Logger.LogToFile(
                    $"Повреждённый или неполный файл в кэше: {fileName}, размер {actualBytes} байт, ожидалось {asset.Size} байт. Файл будет скачан заново.",
                    true);
                File.Delete(destination);
            }

            return await _downloader.DownloadFileWithRetry(
                asset.Url,
                destination,
                progress,
                asset.Size);
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
