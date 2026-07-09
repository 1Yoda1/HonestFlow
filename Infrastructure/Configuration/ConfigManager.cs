using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private const string DefaultYandexPublicKey = "https://disk.360.yandex.ru/d/sngNP8yBz9weWA";
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

            return RemoteConfig.LoadSupportMailSettings();
        }

        public static void InitYandexDiskDownloader()
        {
            _downloader ??= new YandexDiskDownloader();
        }

        public static string GetYandexPublicKey()
        {
            string publicKey = Environment.GetEnvironmentVariable("HONESTFLOW_YANDEX_PUBLIC_KEY");
            if (!string.IsNullOrWhiteSpace(publicKey))
                return publicKey.Trim();

            if (File.Exists(AppPaths.YandexPublicKeyFile))
            {
                publicKey = File.ReadAllText(AppPaths.YandexPublicKeyFile).Trim();
                if (!string.IsNullOrWhiteSpace(publicKey))
                    return publicKey;
            }

            if (File.Exists(AppPaths.YandexPublicUrlFile))
            {
                publicKey = File.ReadAllText(AppPaths.YandexPublicUrlFile).Trim();
                if (!string.IsNullOrWhiteSpace(publicKey))
                    return publicKey;
            }

            return DefaultYandexPublicKey;
        }

        public static async Task<bool> DownloadInstallerIfNeeded(string fileName, IProgress<int> progress)
        {
            InitYandexDiskDownloader();

            var assets = await _downloader.GetReleaseAssets();
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

            if (Directory.Exists(AppPaths.RemoteInstallersCacheFolder) && Directory.EnumerateFiles(AppPaths.RemoteInstallersCacheFolder).Any())
                return AppPaths.RemoteInstallersCacheFolder;

            return AppPaths.BaseFolder;
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
