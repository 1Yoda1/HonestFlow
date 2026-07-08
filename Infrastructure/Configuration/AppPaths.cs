using System;
using System.IO;
using System.Linq;

namespace HonestFlow.Infrastructure.Configuration
{
    /// <summary>
    /// Единая карта папок приложения.
    /// Если нужно поменять место логов, кэша или конфигов — править здесь, а не искать пути по всему проекту.
    /// </summary>
    public static class AppPaths
    {
        public static string BaseFolder => AppDomain.CurrentDomain.BaseDirectory;
        public static string LocalIpsFile => Path.Combine(BaseFolder, "ips_encrypted.json");
        public static string LocalVersionsFile => Path.Combine(BaseFolder, "versions.json");
        public static string YandexPublicKeyFile => Path.Combine(BaseFolder, "yandex_public_key.txt");
        public static string YandexPublicUrlFile => Path.Combine(BaseFolder, "yandex_public_url.txt");
        public static string DistrFolder => Path.Combine(BaseFolder, "Distr");
        public static string YandexDiskCacheFolder => Path.Combine(BaseFolder, "YandexDiskCache");
        public static string LegacyRemoteCacheFolder => Path.Combine(BaseFolder, "Git" + "HubCache");
        public static string RemoteInstallersCacheFolder
        {
            get
            {
                if (Directory.Exists(YandexDiskCacheFolder) && Directory.EnumerateFiles(YandexDiskCacheFolder).Any())
                    return YandexDiskCacheFolder;

                if (Directory.Exists(LegacyRemoteCacheFolder) && Directory.EnumerateFiles(LegacyRemoteCacheFolder).Any())
                    return LegacyRemoteCacheFolder;

                return YandexDiskCacheFolder;
            }
        }

        public static string ResolveRemoteInstallerPath(string fileName)
        {
            string yandexPath = Path.Combine(YandexDiskCacheFolder, fileName);
            if (File.Exists(yandexPath))
                return yandexPath;

            string legacyPath = Path.Combine(LegacyRemoteCacheFolder, fileName);
            if (File.Exists(legacyPath))
                return legacyPath;

            return yandexPath;
        }

        public static string GetRemoteInstallerDownloadPath(string fileName)
        {
            string existingPath = ResolveRemoteInstallerPath(fileName);
            if (File.Exists(existingPath))
                return existingPath;

            return Path.Combine(YandexDiskCacheFolder, fileName);
        }

        public static string ProgramDataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HonestFlow");
        public static string LogsFolder => Path.Combine(ProgramDataFolder, "logs");
        public static string DiagnosticsFolder => Path.Combine(ProgramDataFolder, "diagnostics");
        public static string RuDesktopStateFile => Path.Combine(ProgramDataFolder, "rudesktop_state.json");

        public static void EnsureRuntimeFolders()
        {
            Directory.CreateDirectory(ProgramDataFolder);
            Directory.CreateDirectory(LogsFolder);
            Directory.CreateDirectory(DiagnosticsFolder);
        }
    }


}
