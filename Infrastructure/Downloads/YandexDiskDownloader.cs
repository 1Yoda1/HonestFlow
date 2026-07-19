using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Configuration;
using Newtonsoft.Json.Linq;

namespace HonestFlow.Infrastructure.Downloads
{
    public class YandexDiskDownloader
    {
        private const string PublicResourcesApi = "https://cloud-api.yandex.net/v1/disk/public/resources";
        private const string PublicDownloadApi = "https://cloud-api.yandex.net/v1/disk/public/resources/download";
        private readonly string _cacheFolder;
        private readonly string _publicKey;
        private Dictionary<string, (string Url, long Size)> _cachedAssets = null;
        private readonly object _cacheLock = new object();

        public YandexDiskDownloader(string cacheFolder = null)
        {
            _cacheFolder = string.IsNullOrWhiteSpace(cacheFolder)
                ? AppPaths.YandexDiskCacheFolder
                : cacheFolder;
            _publicKey = ConfigManager.GetYandexPublicKey();

            if (!Directory.Exists(_cacheFolder))
                Directory.CreateDirectory(_cacheFolder);
        }

        public async Task<Dictionary<string, (string Url, long Size)>> GetReleaseAssets(bool forceRefresh = false)
        {
            if (!forceRefresh && _cachedAssets != null)
                return _cachedAssets;

            using var client = CreateClient(TimeSpan.FromSeconds(60));
            string listUrl = BuildPublicResourcesUrl();
            string resourcesJson = await client.GetStringAsync(listUrl);
            var root = JObject.Parse(resourcesJson);
            var items = root["_embedded"]?["items"] as JArray;

            if (items == null)
                throw new InvalidDataException("Yandex Disk public folder does not contain an item list.");

            var assets = new Dictionary<string, (string Url, long Size)>(StringComparer.OrdinalIgnoreCase);
            foreach (JToken item in items)
            {
                string type = (string)item["type"];
                if (!string.Equals(type, "file", StringComparison.OrdinalIgnoreCase))
                    continue;

                string name = (string)item["name"];
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                long size = (long?)item["size"] ?? 0;
                string url = (string)item["file"];

                if (string.IsNullOrWhiteSpace(url))
                    url = await GetDownloadUrl(client, "/" + name);

                assets[name] = (url, size);
                Logger.LogToFile($"Yandex Disk asset found: {name}");
            }

            lock (_cacheLock)
            {
                _cachedAssets = assets;
            }

            return assets;
        }

        public async Task<bool> DownloadFile(
            string downloadUrl,
            string destinationPath,
            IProgress<int> progress,
            long expectedBytes,
            long maximumBytes = long.MaxValue,
            CancellationToken cancellationToken = default)
        {
            string temporaryPath = destinationPath + ".download";

            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);

                using var client = CreateClient(TimeSpan.FromMinutes(10));

                using var response = await client.GetAsync(
                    downloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                if (totalBytes > maximumBytes)
                    throw new InvalidDataException($"Response is too large: {totalBytes} bytes.");

                using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;
                    if (totalRead > maximumBytes)
                        throw new InvalidDataException($"Response exceeded {maximumBytes} bytes.");

                    if (totalBytes > 0 && progress != null)
                    {
                        int percent = (int)((double)totalRead / totalBytes * 100);
                        progress.Report(percent);
                    }
                }

                await fileStream.FlushAsync();
                await fileStream.DisposeAsync();

                long requiredBytes = expectedBytes > 0 ? expectedBytes : totalBytes;
                if (requiredBytes > 0 && totalRead != requiredBytes)
                {
                    throw new InvalidDataException(
                        $"Downloaded file size mismatch: got {totalRead} bytes, expected {requiredBytes} bytes");
                }

                File.Move(temporaryPath, destinationPath, true);

                Logger.LogToFile($"Downloaded: {Path.GetFileName(destinationPath)}");
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch (Exception cleanupEx)
                {
                    Logger.LogToFile(
                        $"Failed to delete temporary file {Path.GetFileName(temporaryPath)}: {cleanupEx.Message}",
                        true);
                }

                Logger.LogToFile($"Download error {Path.GetFileName(destinationPath)}: {ex.Message}", true);
                return false;
            }
        }

        public async Task<bool> DownloadFileWithRetry(
            string downloadUrl,
            string destinationPath,
            IProgress<int> progress,
            long expectedBytes,
            int maxRetries = 3,
            long maximumBytes = long.MaxValue,
            CancellationToken cancellationToken = default)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                Logger.LogToFile($"Download attempt {attempt}/{maxRetries}: {Path.GetFileName(destinationPath)}");

                bool success = await DownloadFile(
                    downloadUrl,
                    destinationPath,
                    progress,
                    expectedBytes,
                    maximumBytes,
                    cancellationToken);
                if (success)
                    return true;

                if (attempt < maxRetries)
                    await Task.Delay(2000 * attempt, cancellationToken);
            }

            Logger.LogToFile($"Failed to download after {maxRetries} attempts: {Path.GetFileName(destinationPath)}", true);
            return false;
        }

        public bool IsFileCached(string fileName, long expectedBytes)
        {
            string path = Path.Combine(_cacheFolder, fileName);
            if (!File.Exists(path) &&
                string.Equals(
                    Path.GetFullPath(_cacheFolder).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(AppPaths.InstallerCacheFolder).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                path = AppPaths.ResolveRemoteInstallerPath(fileName);
            }

            if (!File.Exists(path))
                return false;

            long actualBytes = new FileInfo(path).Length;
            return expectedBytes <= 0 || actualBytes == expectedBytes;
        }

        internal static HttpClient CreateClient(TimeSpan timeout)
        {
            var client = new HttpClient { Timeout = timeout };
            client.DefaultRequestHeaders.Add("User-Agent", "HonestFlow/2.1 YandexDisk");
            return client;
        }

        internal static string BuildPublicResourcesUrl(string publicKey = null)
        {
            return PublicResourcesApi +
                "?public_key=" + Uri.EscapeDataString(publicKey ?? ConfigManager.GetYandexPublicKey()) +
                "&limit=1000";
        }

        internal static string BuildPublicDownloadUrl(string path, string publicKey = null)
        {
            return PublicDownloadApi +
                "?public_key=" + Uri.EscapeDataString(publicKey ?? ConfigManager.GetYandexPublicKey()) +
                "&path=" + Uri.EscapeDataString(path);
        }

        internal async Task<string> GetDownloadUrl(HttpClient client, string path)
        {
            string json = await client.GetStringAsync(BuildPublicDownloadUrl(path, _publicKey));
            var payload = JObject.Parse(json);
            return (string)payload["href"];
        }
    }
}
