using System;
using System.Collections.Generic;
using System.Net.Http;
using HonestFlow.Models;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Configuration
{
    /// <summary>
    /// Чтение конфигов из latest GitHub Release.
    /// Отдельный класс, чтобы UI и установщик не знали деталей GitHub API.
    /// </summary>
    public class RemoteConfigRepository
    {
        private const string GitHubOwner = "1Yoda1";
        private const string GitHubRepo = "HonestFlow";
        private const string UserAgent = "HonestFlow/1.3";

        private List<IPData> _cachedIps;
        private VersionsData _cachedVersions;

        public (bool Success, List<IPData> Ips, VersionsData Versions) LoadAll()
        {
            try
            {
                using var client = CreateClient();
                dynamic release = LoadLatestRelease(client);

                string ipsUrl = FindAssetUrl(release, "ips_encrypted.json");
                string versionsUrl = FindAssetUrl(release, "versions.json");

                if (ipsUrl == null || versionsUrl == null)
                    return (false, null, null);

                string encryptedIps = client.GetStringAsync(ipsUrl).GetAwaiter().GetResult();
                string decryptedIps = ObfuscationService.Deobfuscate(encryptedIps);

                string versionsJson = client.GetStringAsync(versionsUrl).GetAwaiter().GetResult();

                _cachedIps = JsonConvert.DeserializeObject<List<IPData>>(decryptedIps) ?? new List<IPData>();
                _cachedVersions = JsonConvert.DeserializeObject<VersionsData>(versionsJson) ?? new VersionsData();

                return (true, _cachedIps, _cachedVersions);
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"Ошибка загрузки конфигов с GitHub: {ex.Message}", true);
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

        public List<IPData> LoadIps()
        {
            if (_cachedIps != null)
                return _cachedIps;

            var result = LoadAll();
            return result.Success ? result.Ips : new List<IPData>();
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            return client;
        }

        private static dynamic LoadLatestRelease(HttpClient client)
        {
            string apiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/tags/installers";
            var releaseJson = client.GetStringAsync(apiUrl).GetAwaiter().GetResult();
            return JsonConvert.DeserializeObject(releaseJson);
        }

        private static string FindAssetUrl(dynamic release, string fileName)
        {
            foreach (var asset in release.assets)
            {
                if ((string)asset.name == fileName)
                    return asset.browser_download_url;
            }
            return null;
        }
    }
}
