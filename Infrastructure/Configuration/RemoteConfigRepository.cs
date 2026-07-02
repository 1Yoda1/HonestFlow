using System;
using System.Collections.Generic;
using System.Net.Http;
using HonestFlow.Infrastructure.Downloads;
using HonestFlow.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HonestFlow.Infrastructure.Configuration
{
    public class RemoteConfigRepository
    {
        private const string IpsFileName = "ips_encrypted.json";
        private const string VersionsFileName = "versions.json";

        private List<IPData> _cachedIps;
        private VersionsData _cachedVersions;

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

        private static string DownloadPublicTextFile(HttpClient client, string fileName)
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
    }
}
