using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HonestFlow.Models;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure
{
    /// <summary>
    /// Управление JSON-файлами конфигурации (ips.json, versions.json)
    /// </summary>
    public static class ConfigManager
    {
        private static readonly string BasePath = AppDomain.CurrentDomain.BaseDirectory;
        private static readonly string IpsPath = Path.Combine(BasePath, "ips.json");
        private static readonly string VersionsPath = Path.Combine(BasePath, "versions.json");

        // ========== Шифрование/дешифрование ==========
        private static string Obfuscate(string plainText)
        {
            var bytes = Encoding.UTF8.GetBytes(plainText);
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)(bytes[i] ^ 0xAA);
            return Convert.ToBase64String(bytes);
        }

        private static string Deobfuscate(string cipherText)
        {
            var bytes = Convert.FromBase64String(cipherText);
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)(bytes[i] ^ 0xAA);
            return Encoding.UTF8.GetString(bytes);
        }

        // ========== IPs ==========
        public static List<IPData> LoadIps()
        {
            if (!File.Exists(IpsPath))
            {
                Logger.LogToFile("❌ ips.json не найден!", true);
                throw new FileNotFoundException("Файл ips.json не найден в папке программы.");
            }

            var encryptedJson = File.ReadAllText(IpsPath);
            var json = Deobfuscate(encryptedJson);
            var ips = JsonConvert.DeserializeObject<List<IPData>>(json);

            if (ips == null || ips.Count == 0)
            {
                throw new InvalidOperationException("ips.json пуст или имеет неверный формат.");
            }

            Logger.LogToFile($"✅ ips.json загружен, найдено {ips.Count} ИП");
            return ips;
        }

        public static void SaveIps(List<IPData> ips)
        {
            var json = JsonConvert.SerializeObject(ips, Formatting.Indented);
            var encryptedJson = Obfuscate(json);
            File.WriteAllText(IpsPath, encryptedJson);
            Logger.LogToFile($"✅ ips.json сохранён ({ips.Count} записей)");
        }

        // ========== Versions ==========
        public static VersionsData LoadVersions()
        {
            if (!File.Exists(VersionsPath))
            {
                Logger.LogToFile("❌ versions.json не найден!", true);
                throw new FileNotFoundException("Файл versions.json не найден в папке программы.");
            }

            var json = File.ReadAllText(VersionsPath);
            var versions = JsonConvert.DeserializeObject<VersionsData>(json);

            if (versions == null)
            {
                throw new InvalidOperationException("versions.json пуст или имеет неверный формат.");
            }

            // Проверка обязательных полей
            if (string.IsNullOrEmpty(versions.LmModule) ||
                string.IsNullOrEmpty(versions.AtolDriver) ||
                string.IsNullOrEmpty(versions.ESM) ||
                string.IsNullOrEmpty(versions.Controller))
            {
                Logger.LogToFile("⚠️ versions.json: не все версии заполнены", true);
            }

            Logger.LogToFile($"✅ versions.json загружен: ЛМ={versions.LmModule}, АТОЛ={versions.AtolDriver}, ЕСМ={versions.ESM}, Контроллер={versions.Controller}");
            return versions;
        }

        public static void SaveVersions(VersionsData versions)
        {
            var json = JsonConvert.SerializeObject(versions, Formatting.Indented);
            File.WriteAllText(VersionsPath, json);
            Logger.LogToFile($"✅ versions.json сохранён");
        }

        // ========== Папка с установщиками ==========
        public static string GetInstallersFolder()
        {
            string distrPath = Path.Combine(BasePath, "Distr");
            return Directory.Exists(distrPath) ? distrPath : BasePath;
        }
    }
}