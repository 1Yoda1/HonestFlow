using ESM_Installer_SPI.Models;
using HonestFlow.Infrastructure;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
                return GetDefaultIps();

            var encryptedJson = File.ReadAllText(IpsPath);
            var json = Deobfuscate(encryptedJson);
            Logger.LogToFile($"Загрузка {Path.GetFileName(IpsPath)}: файл найден");
            return JsonConvert.DeserializeObject<List<IPData>>(json) ?? GetDefaultIps();
        }

        public static void SaveIps(List<IPData> ips)
        {
            var json = JsonConvert.SerializeObject(ips, Formatting.Indented);
            var encryptedJson = Obfuscate(json);
            File.WriteAllText(IpsPath, encryptedJson);
        }

        // ========== Versions ==========
        public static VersionsData LoadVersions()
        {
            if (!File.Exists(VersionsPath))
                return GetDefaultVersions();
            var json = File.ReadAllText(VersionsPath);
            return JsonConvert.DeserializeObject<VersionsData>(json) ?? GetDefaultVersions();
        }

        public static void SaveVersions(VersionsData versions)
        {
            var json = JsonConvert.SerializeObject(versions, Formatting.Indented);
            File.WriteAllText(VersionsPath, json);
        }

        // ========== Папка с установщиками ==========
        public static string GetInstallersFolder()
        {
            string distrPath = Path.Combine(BasePath, "Distr");
            return Directory.Exists(distrPath) ? distrPath : BasePath;
        }

        // ========== Дефолтные значения ==========
        private static List<IPData> GetDefaultIps()
        {
            return new List<IPData>
            {
                new IPData { Name = "ИП Кураев", Password = "kuraev123", Token = "631ace8b-43aa-4490-9f4f-f404dff01d83", Inn = "170109778389", Architecture = "x64" },
                new IPData { Name = "ИП Бабкин", Password = "babkin456", Token = "e6c03c42-2186-4ae8-adc7-878be4c56f5d", Inn = "550409070683", Architecture = "x64" }
            };
        }

        private static VersionsData GetDefaultVersions()
        {
            return new VersionsData
            {
                lm_module = "2.5.1-2",
                atol_driver = "10.10.8.23",
                esm = "1.6.1.2",
                controller = "1.6.1.0"
            };
        }
    }
}