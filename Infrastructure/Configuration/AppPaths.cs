using System;
using System.IO;

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
        public static string GitHubCacheFolder => Path.Combine(BaseFolder, "GitHubCache");
        public static string ProgramDataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HonestFlow");
        public static string LogsFolder => Path.Combine(ProgramDataFolder, "logs");
        public static string DiagnosticsFolder => Path.Combine(ProgramDataFolder, "diagnostics");

        public static void EnsureRuntimeFolders()
        {
            Directory.CreateDirectory(ProgramDataFolder);
            Directory.CreateDirectory(LogsFolder);
            Directory.CreateDirectory(DiagnosticsFolder);
        }
    }


}
