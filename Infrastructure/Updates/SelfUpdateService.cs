using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Infrastructure.Downloads;
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

namespace HonestFlow.Infrastructure.Updates
{
    public class SelfUpdateService
    {
        private const string UpdateAssetName = "HonestFlow.exe";
        private readonly IUserDialogService _dialogService;

        public SelfUpdateService(IUserDialogService dialogService = null)
        {
            _dialogService = dialogService ?? new WinFormsDialogService();
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

                Version downloadedVersion = GetExecutableVersion(newExePath);
                Logger.Info($"Auto-update: downloaded exe version {downloadedVersion}", nameof(SelfUpdateService));

                if (downloadedVersion < latestVersion)
                {
                    Logger.Error(
                        $"Auto-update: downloaded exe version {downloadedVersion} is lower than advertised {latestVersion}",
                        nameof(SelfUpdateService));
                    _dialogService.ShowError(
                        $"Скачанный HonestFlow.exe имеет версию {downloadedVersion}, а папка/манифест обновления объявляет {latestVersion}.\n\nПроверьте, что в папку {latest.Version} загружен exe, собранный с этой же новой версией.",
                        "Ошибка обновления");
                    return false;
                }

                CreateAndRunUpdateScript(newExePath, backupPath);

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
            var manifest = await TryLoadUpdateFromVersionFolder(client);
            if (manifest == null)
                return null;

            string assetName = manifest.AssetName ?? UpdateAssetName;
            if (string.IsNullOrWhiteSpace(manifest.Version))
                return null;

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
                DownloadUrl = downloadUrl
            };
        }

        private async Task DownloadFile(string url, string destinationPath)
        {
            using var client = YandexDiskDownloader.CreateClient(TimeSpan.FromMinutes(5));

            using var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            await using var input = await response.Content.ReadAsStreamAsync();
            await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

            await input.CopyToAsync(output);
        }

        private void CreateAndRunUpdateScript(string newExePath, string backupPath)
        {
            string runningExe = Environment.ProcessPath;
            string targetExe = ResolveCanonicalUpdateTarget(runningExe);
            string appDir = Path.GetDirectoryName(targetExe) ?? AppPaths.BaseFolder.TrimEnd('\\');
            int currentPid = Environment.ProcessId;

            string scriptPath = Path.Combine(AppPaths.ProgramDataFolder, "update", "apply_update.ps1");
            string scriptLogPath = Path.Combine(AppPaths.ProgramDataFolder, "update", "apply_update.log");
            string backupExe = Path.Combine(backupPath, "HonestFlow.backup.exe");
            string script = BuildUpdateScript(
                runningExe,
                targetExe,
                newExePath,
                backupPath,
                backupExe,
                appDir,
                scriptLogPath,
                currentPid);

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

        private static string ResolveCanonicalUpdateTarget(string runningExe)
        {
            if (string.IsNullOrWhiteSpace(runningExe))
                throw new ArgumentException("Running executable path is required.", nameof(runningExe));

            string directory = Path.GetDirectoryName(runningExe);
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Running executable directory is unavailable.", nameof(runningExe));

            return Path.Combine(directory, UpdateAssetName);
        }

        private static string BuildUpdateScript(
            string runningExe,
            string targetExe,
            string newExePath,
            string backupPath,
            string backupExe,
            string appDir,
            string scriptLogPath,
            int currentPid)
        {
            string runningExePs = ToPowerShellSingleQuotedString(runningExe);
            string targetExePs = ToPowerShellSingleQuotedString(targetExe);
            string newExePathPs = ToPowerShellSingleQuotedString(newExePath);
            string backupPathPs = ToPowerShellSingleQuotedString(backupPath);
            string backupExePs = ToPowerShellSingleQuotedString(backupExe);
            string appDirPs = ToPowerShellSingleQuotedString(appDir);
            string scriptLogPathPs = ToPowerShellSingleQuotedString(scriptLogPath);

            return $@"
$ErrorActionPreference = 'Stop'
$log = {scriptLogPathPs}
$runningExe = {runningExePs}
$targetExe = {targetExePs}
$newExe = {newExePathPs}
$backupDir = {backupPathPs}
$backupExe = {backupExePs}
$appDir = {appDirPs}
$pidToWait = {currentPid}
$backupCreated = $false

function Write-UpdateLog([string]$message) {{
    Add-Content -LiteralPath $log -Value ""[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')] $message"" -Encoding UTF8
}}

try {{
    Set-Content -LiteralPath $log -Value ""[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')] Updating HonestFlow..."" -Encoding UTF8
    Write-UpdateLog ""Running exe: $runningExe""
    Write-UpdateLog ""Target exe: $targetExe""
    Write-UpdateLog ""New exe: $newExe""
    Write-UpdateLog ""Backup exe: $backupExe""

    Start-Sleep -Seconds 2
    while (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) {{
        Write-UpdateLog ""Waiting for process $pidToWait""
        Start-Sleep -Seconds 1
    }}

    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

    if (Test-Path -LiteralPath $targetExe) {{
        Write-UpdateLog ""Backing up target exe""
        Copy-Item -LiteralPath $targetExe -Destination $backupExe -Force
        $backupCreated = $true
    }}

    Write-UpdateLog ""Replacing canonical exe""
    Copy-Item -LiteralPath $newExe -Destination $targetExe -Force

    Write-UpdateLog ""Update applied successfully""
    Start-Process -FilePath $targetExe -WorkingDirectory $appDir
    exit 0
}}
catch {{
    Write-UpdateLog ""Update failed: $($_.Exception.Message)""
    try {{
        if ($backupCreated -and (Test-Path -LiteralPath $backupExe)) {{
            Write-UpdateLog ""Restoring backup""
            Copy-Item -LiteralPath $backupExe -Destination $targetExe -Force
        }}
        elseif (Test-Path -LiteralPath $targetExe) {{
            Write-UpdateLog ""Removing incomplete target""
            Remove-Item -LiteralPath $targetExe -Force
        }}
    }}
    catch {{
        Write-UpdateLog ""Backup restore failed: $($_.Exception.Message)""
    }}

    try {{
        if (Test-Path -LiteralPath $targetExe) {{
            Start-Process -FilePath $targetExe -WorkingDirectory $appDir
        }}
        else {{
            Start-Process -FilePath $runningExe -WorkingDirectory $appDir
        }}
    }}
    catch {{
        Write-UpdateLog ""Restart failed: $($_.Exception.Message)""
    }}

    exit 1
}}
";
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
                    AssetName = versionFolder.Name.Trim('/') + "/" + UpdateAssetName
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
