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
        public static string LocalSupportMailFile => Path.Combine(BaseFolder, "support_mail_encrypted.json");
        public static string YandexPublicKeyFile => Path.Combine(BaseFolder, "yandex_public_key.txt");
        public static string YandexPublicUrlFile => Path.Combine(BaseFolder, "yandex_public_url.txt");
        public static string DistrFolder => Path.Combine(BaseFolder, "Distr");
        public static string LegacyYandexDiskCacheFolder => Path.Combine(BaseFolder, "YandexDiskCache");
        public static string LegacyRemoteCacheFolder => Path.Combine(BaseFolder, "Git" + "HubCache");
        public static string InstallerCacheFolder => Path.Combine(ProgramDataFolder, "cache", "installers");
        public static string LegacyInstallerCacheLocationsFile =>
            Path.Combine(ProgramDataFolder, "cache", "legacy-installer-locations.json");
        public static string YandexDiskCacheFolder => InstallerCacheFolder;
        public static string RemoteInstallersCacheFolder
        {
            get
            {
                foreach (string folder in EnumerateInstallerCacheReadFolders())
                {
                    if (ContainsFiles(folder))
                        return folder;
                }

                return InstallerCacheFolder;
            }
        }

        public static string ResolveRemoteInstallerPath(string fileName)
        {
            ValidateInstallerFileName(fileName);
            foreach (string folder in EnumerateInstallerCacheReadFolders())
            {
                string candidate = Path.Combine(folder, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return Path.Combine(InstallerCacheFolder, fileName);
        }

        public static string GetRemoteInstallerDownloadPath(string fileName)
        {
            ValidateInstallerFileName(fileName);
            return Path.Combine(InstallerCacheFolder, fileName);
        }

        public static string ProgramDataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HonestFlow");
        public static string LogsFolder => Path.Combine(ProgramDataFolder, "logs");
        public static string DiagnosticsFolder => Path.Combine(ProgramDataFolder, "diagnostics");
        public static string LicenseCacheFolder => Path.Combine(ProgramDataFolder, "license-cache");
        public static string DeviceIdentityFolder => Path.Combine(ProgramDataFolder, "device-identity");
        public static string DeviceIdentityFile => Path.Combine(DeviceIdentityFolder, "device-identity.dpapi");
        public static string RuDesktopStateFile => Path.Combine(ProgramDataFolder, "rudesktop_state.json");
        public static string RuDesktopInstallerCacheFolder => Path.Combine(ProgramDataFolder, "rudesktop-installer");
        public static string RuDesktopInstallerLogFile => Path.Combine(LogsFolder, "rudesktop-msi-install.log");
        public static string DotNetRuntimeCacheFolder => Path.Combine(ProgramDataFolder, "dotnet-runtime");
        public static string PointAddressFile => Path.Combine(ProgramDataFolder, "point-address.json");

        public static void EnsureRuntimeFolders()
        {
            Directory.CreateDirectory(ProgramDataFolder);
            Directory.CreateDirectory(LogsFolder);
            Directory.CreateDirectory(DiagnosticsFolder);
            Directory.CreateDirectory(InstallerCacheFolder);
            Directory.CreateDirectory(RuDesktopInstallerCacheFolder);
            Directory.CreateDirectory(DotNetRuntimeCacheFolder);
        }

        private static System.Collections.Generic.IEnumerable<string> EnumerateInstallerCacheReadFolders()
        {
            yield return InstallerCacheFolder;
            yield return LegacyYandexDiskCacheFolder;
            yield return LegacyRemoteCacheFolder;

            foreach (string folder in new InstallerCacheLocationStore().ReadLocations())
            {
                if (!string.Equals(folder, InstallerCacheFolder, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(folder, LegacyYandexDiskCacheFolder, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(folder, LegacyRemoteCacheFolder, StringComparison.OrdinalIgnoreCase))
                {
                    yield return folder;
                }
            }
        }

        private static bool ContainsFiles(string folder)
        {
            try
            {
                return Directory.Exists(folder) && Directory.EnumerateFiles(folder).Any();
            }
            catch
            {
                return false;
            }
        }

        private static void ValidateInstallerFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) ||
                !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
            {
                throw new ArgumentException("Ожидалось только имя файла установщика.", nameof(fileName));
            }
        }
    }


}
