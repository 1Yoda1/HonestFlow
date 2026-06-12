using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Configuration;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Downloads
{
    public class GitHubDownloader
    {
        private const string OWNER = "1Yoda1";
        private const string REPO = "HonestFlow";
        private readonly string _cacheFolder;
        private Dictionary<string, string> _cachedAssets = null;
        private readonly object _cacheLock = new object();

        public GitHubDownloader()
        {
            _cacheFolder = AppPaths.GitHubCacheFolder;
            if (!Directory.Exists(_cacheFolder))
                Directory.CreateDirectory(_cacheFolder);
        }

        public async Task<Dictionary<string, string>> GetReleaseAssets(bool forceRefresh = false)
        {
            if (!forceRefresh && _cachedAssets != null)
                return _cachedAssets;

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "HonestFlow/1.3");
            client.Timeout = TimeSpan.FromSeconds(10);

            string apiUrl = $"https://api.github.com/repos/{OWNER}/{REPO}/releases/tags/installers";
            var releaseJson = await client.GetStringAsync(apiUrl);
            dynamic release = JsonConvert.DeserializeObject(releaseJson);

            var assets = new Dictionary<string, string>();
            foreach (var asset in release.assets)
            {
                string name = asset.name;
                string url = asset.browser_download_url;
                assets[name] = url;
                Logger.LogToFile($"📦 Найден asset: {name}");
            }

            lock (_cacheLock)
            {
                _cachedAssets = assets;
            }

            return assets;
        }

        public async Task<bool> DownloadFile(string downloadUrl, string destinationPath, IProgress<int> progress)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "HonestFlow/1.3");
                client.Timeout = TimeSpan.FromMinutes(10);

                using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;

                    if (totalBytes > 0 && progress != null)
                    {
                        int percent = (int)((double)totalRead / totalBytes * 100);
                        progress.Report(percent);
                    }
                }

                Logger.LogToFile($"✅ Скачан: {Path.GetFileName(destinationPath)}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"❌ Ошибка скачивания {Path.GetFileName(destinationPath)}: {ex.Message}", true);
                return false;
            }
        }

        public async Task<bool> DownloadFileWithRetry(string downloadUrl, string destinationPath, IProgress<int> progress, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                Logger.LogToFile($"⬇ Попытка {attempt}/{maxRetries}: {Path.GetFileName(destinationPath)}");

                bool success = await DownloadFile(downloadUrl, destinationPath, progress);
                if (success)
                    return true;

                if (attempt < maxRetries)
                {
                    await Task.Delay(2000 * attempt); // 2, 4, 6 секунд
                }
            }

            Logger.LogToFile($"❌ Не удалось скачать после {maxRetries} попыток: {Path.GetFileName(destinationPath)}", true);
            return false;
        }

        public string GetCachedFile(string fileName)
        {
            string path = Path.Combine(_cacheFolder, fileName);
            return File.Exists(path) ? path : null;
        }

        public bool IsFileCached(string fileName)
        {
            return File.Exists(Path.Combine(_cacheFolder, fileName));
        }

        public long GetTotalInstallerSize(List<string> fileNames)
        {
            // Можно реализовать получение размеров из GitHub API
            // Для простоты вернём примерную оценку
            return 350 * 1024 * 1024; // ~350 MB
        }

        public long GetFreeSpace()
        {
            string rootPath = Path.GetPathRoot(_cacheFolder);
            var driveInfo = new DriveInfo(rootPath);
            return driveInfo.AvailableFreeSpace;
        }

        public bool HasEnoughSpace(long requiredBytes)
        {
            long freeSpace = GetFreeSpace();
            long requiredWithBuffer = requiredBytes + (100 * 1024 * 1024); // +100 MB запас
            return freeSpace >= requiredWithBuffer;
        }
    }
}