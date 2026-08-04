using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Infrastructure.Downloads;
using HonestFlow.Infrastructure.Licensing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;

namespace HonestFlow.Infrastructure.Updates
{
    public class SelfUpdateService
    {
        private const string UpdateManifestName = "update.json";
        private const string UpdateSignatureName = "update.json.sig";
        private readonly IUserDialogService _dialogService;
        private readonly SelfUpdateManifestVerifier _manifestVerifier;

        public SelfUpdateService(
            IUserDialogService dialogService = null,
            SelfUpdateManifestVerifier manifestVerifier = null)
        {
            _dialogService = dialogService ?? new WinFormsDialogService();
            _manifestVerifier = manifestVerifier ?? new SelfUpdateManifestVerifier(
                new EcdsaLicenseSignatureVerifier(
                    new LicensePublicKeyRegistry(EmbeddedLicenseTrust.CreateKeyRegistry())));
        }

        public async Task<bool> CheckDownloadAndRunUpdateIfNeeded()
        {
            try
            {
                var latest = await GetLatestReleaseInfo();
                if (latest == null)
                {
                    Logger.Warning("Auto-update: HonestFlow.exe info was not found on Yandex Disk", nameof(SelfUpdateService));
                    return false;
                }

                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
                Version latestVersion = NormalizeVersion(latest.Version);

                Logger.Info($"Auto-update: current version {currentVersion}, latest {latestVersion}", nameof(SelfUpdateService));

                if (latestVersion <= currentVersion)
                    return false;

                _dialogService.ShowInformation(
                    $"Доступна обязательная версия HonestFlow: {latestVersion}.\n\n" +
                    "Приложение сейчас скачает и установит обновление.",
                    "Обновление HonestFlow");

                string updateRoot = Path.Combine(AppPaths.ProgramDataFolder, "update");
                Directory.CreateDirectory(updateRoot);

                string newExePath = Path.Combine(updateRoot, "HonestFlow.new.exe");
                string backupPath = Path.Combine(updateRoot, "backup");

                if (File.Exists(newExePath))
                    File.Delete(newExePath);

                await DownloadFile(latest.DownloadUrl, newExePath);

                if (!File.Exists(newExePath) || new FileInfo(newExePath).Length == 0)
                {
                    Logger.Error("Auto-update: downloaded HonestFlow.exe is empty or missing", nameof(SelfUpdateService));
                    _dialogService.ShowError("Обновление скачано некорректно.", "Ошибка обновления");
                    return false;
                }

                await _manifestVerifier.VerifyDownloadedFileAsync(newExePath, latest.Manifest);

                Version downloadedVersion = GetExecutableVersion(newExePath);
                Logger.Info($"Auto-update: downloaded exe version {downloadedVersion}", nameof(SelfUpdateService));

                if (downloadedVersion != latestVersion)
                {
                    Logger.Error(
                        $"Auto-update: downloaded exe version {downloadedVersion} is lower than advertised {latestVersion}",
                        nameof(SelfUpdateService));
                    _dialogService.ShowError(
                        $"Скачанный HonestFlow.exe имеет версию {downloadedVersion}, а папка/манифест обновления объявляет {latestVersion}.\n\nПроверьте, что в папку {latest.Version} загружен exe, собранный с этой же новой версией.",
                        "Ошибка обновления");
                    return false;
                }

                CreateAndRunUpdateScript(newExePath, backupPath, latest.Manifest);

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Ошибка автообновления", nameof(SelfUpdateService));
                return false;
            }
        }

        private async Task<SelfUpdateInfo> GetLatestReleaseInfo()
        {
            using var client = YandexDiskDownloader.CreateClient(TimeSpan.FromSeconds(30));
            var releaseFolder = await TryLoadUpdateFromVersionFolder(client);
            if (releaseFolder == null)
                return null;

            string folderName = releaseFolder.AssetName?.Trim('/');
            string manifestPath = $"/{folderName}/{UpdateManifestName}";
            string signaturePath = $"/{folderName}/{UpdateSignatureName}";
            string manifestUrl = await TryGetYandexDownloadUrl(client, manifestPath);
            string signatureUrl = await TryGetYandexDownloadUrl(client, signaturePath);
            if (string.IsNullOrWhiteSpace(manifestUrl) || string.IsNullOrWhiteSpace(signatureUrl))
            {
                Logger.Warning("Auto-update: signed update manifest was not found", nameof(SelfUpdateService));
                return null;
            }

            byte[] manifestBytes = await DownloadBytes(client, manifestUrl, 64 * 1024);
            byte[] signatureBytes = await DownloadBytes(client, signatureUrl, 16 * 1024);
            SelfUpdateManifest manifest = _manifestVerifier.VerifyManifest(manifestBytes, signatureBytes);

            Version folderVersion = NormalizeVersion(releaseFolder.Version);
            Version manifestVersion = NormalizeVersion(manifest.Version);
            if (folderVersion != manifestVersion)
                throw new InvalidDataException("UpdateManifestFolderVersionMismatch");

            string assetName = folderName + "/" + manifest.AssetName;
            string downloadUrl = await TryGetYandexDownloadUrl(client, "/" + assetName);
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                Logger.Warning($"Auto-update: update file not found on Yandex Disk: /{assetName}", nameof(SelfUpdateService));
                return null;
            }

            return new SelfUpdateInfo
            {
                Version = manifest.Version,
                AssetName = assetName,
                DownloadUrl = downloadUrl,
                Manifest = manifest
            };
        }

        private static async Task<byte[]> DownloadBytes(HttpClient client, string url, int maximumBytes)
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > maximumBytes)
                throw new InvalidDataException("UpdateMetadataTooLarge");

            await using Stream input = await response.Content.ReadAsStreamAsync();
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                if (output.Length + read > maximumBytes)
                    throw new InvalidDataException("UpdateMetadataTooLarge");
                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }

        private async Task DownloadFile(string url, string destinationPath)
        {
            using var client = YandexDiskDownloader.CreateClient(TimeSpan.FromMinutes(5));

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength > SelfUpdateManifestVerifier.MaximumUpdateBytes)
                throw new InvalidDataException("UpdateAssetTooLarge");

            await using var input = await response.Content.ReadAsStreamAsync();
            await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                total += read;
                if (total > SelfUpdateManifestVerifier.MaximumUpdateBytes)
                    throw new InvalidDataException("UpdateAssetTooLarge");
                await output.WriteAsync(buffer.AsMemory(0, read));
            }
        }

        private void CreateAndRunUpdateScript(
            string newExePath,
            string backupPath,
            SelfUpdateManifest manifest)
        {
            string currentExe = Environment.ProcessPath;
            string appDir = AppPaths.BaseFolder.TrimEnd('\\');
            int currentPid = Environment.ProcessId;

            string scriptPath = Path.Combine(AppPaths.ProgramDataFolder, "update", "apply_update.ps1");
            string scriptLogPath = Path.Combine(AppPaths.ProgramDataFolder, "update", "apply_update.log");
            string backupExe = Path.Combine(backupPath, "HonestFlow.backup.exe");
            string currentExePs = ToPowerShellSingleQuotedString(currentExe);
            string newExePathPs = ToPowerShellSingleQuotedString(newExePath);
            string backupPathPs = ToPowerShellSingleQuotedString(backupPath);
            string backupExePs = ToPowerShellSingleQuotedString(backupExe);
            string appDirPs = ToPowerShellSingleQuotedString(appDir);
            string scriptLogPathPs = ToPowerShellSingleQuotedString(scriptLogPath);
            string expectedSha256Ps = ToPowerShellSingleQuotedString(manifest.Sha256);

            string script = $@"
$ErrorActionPreference = 'Stop'
$log = {scriptLogPathPs}
$currentExe = {currentExePs}
$newExe = {newExePathPs}
$backupDir = {backupPathPs}
$backupExe = {backupExePs}
$appDir = {appDirPs}
$pidToWait = {currentPid}
$expectedSize = {manifest.Size}
$expectedSha256 = {expectedSha256Ps}

function Write-UpdateLog([string]$message) {{
    Add-Content -LiteralPath $log -Value ""[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')] $message"" -Encoding UTF8
}}

try {{
    Set-Content -LiteralPath $log -Value ""[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')] Updating HonestFlow..."" -Encoding UTF8
    Write-UpdateLog ""Current exe: $currentExe""
    Write-UpdateLog ""New exe: $newExe""
    Write-UpdateLog ""Backup exe: $backupExe""

    Start-Sleep -Seconds 2
    while (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) {{
        Write-UpdateLog ""Waiting for process $pidToWait""
        Start-Sleep -Seconds 1
    }}

    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

    $newExeInfo = Get-Item -LiteralPath $newExe
    if ($newExeInfo.Length -ne $expectedSize) {{
        throw ""Update size changed after verification""
    }}
    $actualSha256 = (Get-FileHash -LiteralPath $newExe -Algorithm SHA256).Hash
    if ($actualSha256 -ne $expectedSha256) {{
        throw ""Update SHA-256 changed after verification""
    }}

    Write-UpdateLog ""Backing up current exe""
    Copy-Item -LiteralPath $currentExe -Destination $backupExe -Force

    Write-UpdateLog ""Replacing current exe""
    Copy-Item -LiteralPath $newExe -Destination $currentExe -Force

    Write-UpdateLog ""Update applied successfully""
    Start-Process -FilePath $currentExe -WorkingDirectory $appDir
    exit 0
}}
catch {{
    Write-UpdateLog ""Update failed: $($_.Exception.Message)""
    try {{
        if (Test-Path -LiteralPath $backupExe) {{
            Write-UpdateLog ""Restoring backup""
            Copy-Item -LiteralPath $backupExe -Destination $currentExe -Force
        }}
    }}
    catch {{
        Write-UpdateLog ""Backup restore failed: $($_.Exception.Message)""
    }}

    try {{
        Start-Process -FilePath $currentExe -WorkingDirectory $appDir
    }}
    catch {{
        Write-UpdateLog ""Restart failed: $($_.Exception.Message)""
    }}

    exit 1
}}
";

            File.WriteAllText(scriptPath, script, Encoding.UTF8);

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = appDir
            });

            System.Windows.Forms.Application.Exit();
        }

        private static string ToPowerShellSingleQuotedString(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "''") + "'";
        }

        private static Version GetExecutableVersion(string exePath)
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
            string versionText = versionInfo.FileVersion ?? versionInfo.ProductVersion;

            if (Version.TryParse(versionText, out var version))
                return version;

            return new Version(0, 0, 0, 0);
        }

        private static Version NormalizeVersion(string tag)
        {
            string cleaned = tag?.Trim().TrimStart('v', 'V') ?? "0.0.0.0";
            return Version.TryParse(cleaned, out var version) ? version : new Version(0, 0, 0, 0);
        }

        private static async Task<SelfUpdateInfo> TryLoadUpdateFromVersionFolder(HttpClient client)
        {
            try
            {
                string json = await client.GetStringAsync(YandexDiskDownloader.BuildPublicResourcesUrl());
                var payload = JObject.Parse(json);
                var items = payload["_embedded"]?["items"] as JArray;

                if (items == null)
                    return null;

                var versionFolder = items
                    .Where(item => string.Equals((string)item["type"], "dir", StringComparison.OrdinalIgnoreCase))
                    .Select(item => new
                    {
                        Name = (string)item["name"],
                        VersionText = ExtractVersion((string)item["name"])
                    })
                    .Where(item => Version.TryParse(item.VersionText, out _))
                    .Select(item => new
                    {
                        item.Name,
                        item.VersionText,
                        Version = Version.Parse(item.VersionText)
                    })
                    .OrderByDescending(item => item.Version)
                    .FirstOrDefault();

                if (versionFolder == null)
                    return null;

                return new SelfUpdateInfo
                {
                    Version = versionFolder.VersionText,
                    AssetName = versionFolder.Name.Trim('/')
                };
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string ExtractVersion(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var match = Regex.Match(text, @"\d+\.\d+\.\d+(?:\.\d+)?");
            return match.Success ? match.Value : null;
        }

        private static async Task<string> TryGetYandexDownloadUrl(HttpClient client, string path)
        {
            try
            {
                string json = await client.GetStringAsync(YandexDiskDownloader.BuildPublicDownloadUrl(path));
                var payload = JObject.Parse(json);
                return (string)payload["href"];
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }
    }
}
