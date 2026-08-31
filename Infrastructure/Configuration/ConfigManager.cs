using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Downloads;
using HonestFlow.Models;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Configuration
{
    /// <summary>
    /// Compatibility facade for configuration and installer downloads.
    /// </summary>
    public static class ConfigManager
    {
        private static readonly LocalConfigRepository LocalConfig = new();
        private static readonly RemoteConfigRepository RemoteConfig = new();
        public const string ProductionYandexPublicKey = "https://disk.360.yandex.ru/d/sngNP8yBz9weWA";
        private static YandexDiskDownloader _downloader;

        public static List<IPData> LoadIps() => LocalConfig.LoadIps();
        public static VersionsData LoadVersions() => LocalConfig.LoadVersions();
        public static (bool Success, List<IPData> Ips, VersionsData Versions) LoadRemoteConfig() => RemoteConfig.LoadAll();
        public static VersionsData LoadRemoteVersions() => RemoteConfig.LoadVersions();
        public static SupportMailSettings LoadSupportMailSettings()
        {
            SupportMailSettings localSettings = LoadLocalSupportMailSettings();
            if (localSettings != null)
                return localSettings;

            SupportMailSettings cachedSettings = LoadSupportMailSettingsFile(AppPaths.CachedSupportMailFile);
            SupportMailSettings remoteSettings = RemoteConfig.LoadSupportMailSettings();
            return remoteSettings ?? cachedSettings;
        }

        public static void InitYandexDiskDownloader()
        {
            _downloader ??= new YandexDiskDownloader();
        }

        public static string GetYandexPublicKey()
        {
            return ProductionYandexPublicKey;
        }

        public static async Task<bool> DownloadInstallerIfNeeded(
            string fileName,
            IProgress<int> progress,
            CancellationToken cancellationToken = default)
        {
            InitYandexDiskDownloader();

            cancellationToken.ThrowIfCancellationRequested();
            var assets = await _downloader.GetReleaseAssets(cancellationToken: cancellationToken);
            if (!assets.TryGetValue(fileName, out var asset))
            {
                Logger.LogToFile($"File not found in Yandex Disk public folder: {fileName}", true);
                return false;
            }

            string destination = AppPaths.GetRemoteInstallerDownloadPath(fileName);

            if (_downloader.IsFileCached(fileName, asset.Size))
            {
                Logger.LogToFile($"File already cached: {fileName}, size: {asset.Size} bytes");
                return true;
            }

            if (File.Exists(destination))
            {
                long actualBytes = new FileInfo(destination).Length;
                Logger.LogToFile(
                    $"Damaged or incomplete cached file: {fileName}, size {actualBytes} bytes, expected {asset.Size} bytes. The file will be downloaded again.",
                    true);
                try
                {
                    File.Delete(destination);
                }
                catch (Exception ex)
                {
                    Logger.LogToFile(
                        $"Failed to delete damaged cached file {fileName}: {ex.Message}. Download will overwrite if possible.",
                        true);
                }
            }

            return await _downloader.DownloadFileWithRetry(
                asset.Url,
                destination,
                progress,
                asset.Size,
                cancellationToken: cancellationToken);
        }

        public static string GetInstallersFolder()
        {
            if (Directory.Exists(AppPaths.DistrFolder))
                return AppPaths.DistrFolder;

            if (Directory.Exists(AppPaths.RemoteInstallersCacheFolder) && Directory.EnumerateFiles(AppPaths.RemoteInstallersCacheFolder).Any())
                return AppPaths.RemoteInstallersCacheFolder;

            return AppPaths.BaseFolder;
        }

        private static SupportMailSettings LoadSupportMailSettingsFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;
                string encryptedJson = File.ReadAllText(path);
                string json = ObfuscationService.Deobfuscate(encryptedJson);
                return JsonConvert.DeserializeObject<SupportMailSettings>(json);
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"Support mail cached config loading error: {ex.Message}", true);
                return null;
            }
        }
        private static SupportMailSettings LoadLocalSupportMailSettings()
        {
            try
            {
                if (!File.Exists(AppPaths.LocalSupportMailFile))
                    return null;

                string encryptedJson = File.ReadAllText(AppPaths.LocalSupportMailFile);
                string json = ObfuscationService.Deobfuscate(encryptedJson);
                return JsonConvert.DeserializeObject<SupportMailSettings>(json);
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"Support mail local config loading error: {ex.Message}", true);
                return null;
            }
        }
    }
}
