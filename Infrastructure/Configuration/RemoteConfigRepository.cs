using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using HonestFlow.Infrastructure.Downloads;
using System.Threading.Tasks;
using HonestFlow.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HonestFlow.Infrastructure.Configuration
{
    public class RemoteConfigRepository
    {
        private const string IpsFileName = "ips_encrypted.json";
        private const string VersionsFileName = "versions.json";
        private const string SupportMailFileName = "support_mail_encrypted.json";

        private List<IPData> _cachedIps;
        private VersionsData _cachedVersions;
        private SupportMailSettings _cachedSupportMailSettings;

        public (bool Success, List<IPData> Ips, VersionsData Versions) LoadAll()
        {
            try
            {
                using var client = YandexDiskDownloader.CreateClient(TimeSpan.FromSeconds(30));

                string encryptedIps = DownloadPublicTextFile(client, IpsFileName);
                string decryptedIps = ObfuscationService.Deobfuscate(encryptedIps);
                string versionsJson = DownloadPublicTextFile(client, VersionsFileName);

                _cachedIps = JsonConvert.DeserializeObject<List<IPData>>(decryptedIps) ?? new List<IPData>();
                _cachedVersions = JsonConvert.DeserializeObject<VersionsData>(versionsJson) ?? new VersionsData();

                return (true, _cachedIps, _cachedVersions);
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"Yandex Disk config loading error: {ex.Message}", true);
                return (false, null, null);
            }
        }

        public VersionsData LoadVersions()
        {
            if (_cachedVersions != null)
                return _cachedVersions;

            var result = LoadAll();
            return result.Success ? result.Versions : new VersionsData();
        }

        public SupportMailSettings LoadSupportMailSettings()
        {
            if (_cachedSupportMailSettings != null)
                return _cachedSupportMailSettings;

            try
            {
                using var client = YandexDiskDownloader.CreateClient(TimeSpan.FromSeconds(30));

                string encryptedJson = DownloadPublicTextFile(client, SupportMailFileName);
                Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.CachedSupportMailFile)!);
                File.WriteAllText(AppPaths.CachedSupportMailFile, encryptedJson);
                string json = ObfuscationService.Deobfuscate(encryptedJson);

                _cachedSupportMailSettings = JsonConvert.DeserializeObject<SupportMailSettings>(json);
                return _cachedSupportMailSettings;
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"Support mail remote config loading error: {ex.Message}", true);
                return null;
            }
        }

        private static string DownloadPublicTextFile(HttpClient client, string fileName)
        {
            Exception lastError = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    string downloadInfoJson = client
                        .GetStringAsync(YandexDiskDownloader.BuildPublicDownloadUrl("/" + fileName))
                        .GetAwaiter()
                        .GetResult();

                    var downloadInfo = JObject.Parse(downloadInfoJson);
                    string href = (string)downloadInfo["href"];
                    if (string.IsNullOrWhiteSpace(href))
                        throw new InvalidOperationException($"Yandex Disk did not return a download URL for {fileName}.");

                    return client.GetStringAsync(href).GetAwaiter().GetResult();
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
                {
                    lastError = ex;
                    if (attempt < 3)
                        System.Threading.Thread.Sleep(500 * attempt);
                }
            }

            throw lastError ?? new InvalidOperationException($"Could not download {fileName}.");
        }
    }
}
