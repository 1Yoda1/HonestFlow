using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Infrastructure.Downloads;

namespace HonestFlow.Infrastructure.Updates
{
    /// <summary>
    /// Applies a self-update from the public HonestFlow-only trusted server endpoint.
    /// Both the download cache and the PowerShell apply step verify server SHA-256.
    /// </summary>
    public sealed class SelfUpdateService
    {
        private const string UpdateAssetName = "HonestFlow.exe";
        private readonly IPublicHonestFlowUpdateClient _updateClient;
        private readonly IUserDialogService _dialogService;
        private readonly VerifiedAssetDownloader _downloader;
        private readonly Func<Version> _currentVersion;
        private readonly Func<string, Version> _downloadedVersion;
        private readonly Action<string, string, string, long?> _applyUpdate;
        private readonly ReleaseManifestVerifier _releaseVerifier;
        private readonly bool _requireSignedManifest;
        private readonly string _updateRoot;

        public SelfUpdateService(
            IPublicHonestFlowUpdateClient updateClient,
            IUserDialogService dialogService,
            VerifiedAssetDownloader downloader = null,
            Func<Version> currentVersion = null,
            Func<string, Version> downloadedVersion = null,
            Action<string, string, string, long?> applyUpdate = null,
            string updateRoot = null,
            ReleaseManifestVerifier releaseVerifier = null,
            bool requireSignedManifest = ReleaseTrustStore.RequireSignedManifests)
        {
            _updateClient = updateClient ?? throw new ArgumentNullException(nameof(updateClient));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _downloader = downloader ?? new VerifiedAssetDownloader();
            _currentVersion = currentVersion ?? (() => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0));
            _downloadedVersion = downloadedVersion ?? GetExecutableVersion;
            _applyUpdate = applyUpdate ?? CreateAndRunUpdateScript;
            _updateRoot = updateRoot ?? Path.Combine(AppPaths.ProgramDataFolder, "update");
            _releaseVerifier = releaseVerifier ?? ReleaseTrustStore.CreateVerifier();
            _requireSignedManifest = requireSignedManifest;
        }

        public async Task<bool> CheckDownloadAndRunUpdateIfNeeded(CancellationToken cancellationToken = default)
        {
            try
            {
                SelfUpdateInfo? latest = await _updateClient.GetCurrentAsync(cancellationToken);
                if (latest is null || string.IsNullOrWhiteSpace(latest.Version))
                    return false;

                Version currentVersion = _currentVersion();
                Version latestVersion = NormalizeVersion(latest.Version);
                if (latestVersion <= currentVersion)
                    return false;

                string runtimeArchitecture = Environment.Is64BitOperatingSystem ? "x64" : "x86";
                TrustedAsset asset = SignedAssetMetadata.Resolve(
                    "HonestFlow", latest.Version, latest.AssetName, latest.DownloadUrl,
                    latest.Sha256, latest.SizeBytes, runtimeArchitecture,
                    latest.ReleaseKeyId, latest.ManifestBase64, latest.ReleaseSignatureBase64,
                    _releaseVerifier, _requireSignedManifest);
                asset.Validate();

                _dialogService.ShowInformation(
                    $"Доступна обязательная версия HonestFlow: {latestVersion}.\n\n" +
                    "Приложение сейчас скачает и установит обновление.",
                    "Обновление HonestFlow");

                // ResponseHeadersRead avoids buffering the executable, so the body needs its own deadline.
                using var downloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                downloadCancellation.CancelAfter(TimeSpan.FromMinutes(10));
                string newExePath = await _downloader.GetVerifiedAsync(
                    asset,
                    _updateRoot,
                    (request, token) => _updateClient.SendAsync(request, token),
                    progress: null,
                    downloadCancellation.Token);

                Version downloadedVersion = _downloadedVersion(newExePath);
                if (downloadedVersion < latestVersion)
                {
                    _dialogService.ShowError(
                        $"Скачанный HonestFlow имеет версию {downloadedVersion}, а сервер объявил {latestVersion}.",
                        "Ошибка обновления");
                    return false;
                }

                _applyUpdate(newExePath, asset.Sha256, latest.Version, asset.SizeBytes);
                return true;
            }
            catch (Exception ex) when (ex is InvalidDataException or HttpRequestException or IOException or UnauthorizedAccessException)
            {
                Logger.LogException(ex, "Автообновление отклонено", nameof(SelfUpdateService));
                _dialogService.ShowError(
                    "Не удалось безопасно проверить файл обновления. Обновление не будет установлено.",
                    "Ошибка обновления");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Ошибка автообновления", nameof(SelfUpdateService));
                return false;
            }
        }

        private static void CreateAndRunUpdateScript(
            string newExePath,
            string expectedSha256,
            string advertisedVersion,
            long? expectedSizeBytes)
        {
            string runningExe = Environment.ProcessPath;
            string targetExe = ResolveCanonicalUpdateTarget(runningExe);
            string appDir = Path.GetDirectoryName(targetExe) ?? AppPaths.BaseFolder.TrimEnd('\\');
            string scriptLogPath = Path.Combine(appDir, "HonestFlow.update.log");
            string backupExe = Path.Combine(appDir, "HonestFlow.backup.exe");
            string script = BuildUpdateScript(
                runningExe, targetExe, newExePath, backupExe, appDir, scriptLogPath,
                Environment.ProcessId, expectedSha256, expectedSizeBytes ?? 0, advertisedVersion);

            Process.Start(BuildPowerShellStartInfo(script, appDir));
            System.Windows.Application.Current.Shutdown();
        }

        private static ProcessStartInfo BuildPowerShellStartInfo(string script, string appDir)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell", "v1.0", "powershell.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = appDir
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-EncodedCommand");
            startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
            return startInfo;
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
            string backupExe,
            string appDir,
            string scriptLogPath,
            int currentPid,
            string expectedSha256,
            long expectedSizeBytes,
            string advertisedVersion)
        {
            return $@"
$ErrorActionPreference = 'Stop'
$log = {ToPowerShellSingleQuotedString(scriptLogPath)}
$runningExe = {ToPowerShellSingleQuotedString(runningExe)}
$targetExe = {ToPowerShellSingleQuotedString(targetExe)}
$newExe = {ToPowerShellSingleQuotedString(newExePath)}
$backupExe = {ToPowerShellSingleQuotedString(backupExe)}
$appDir = {ToPowerShellSingleQuotedString(appDir)}
$pidToWait = {currentPid}
$expectedSha256 = {ToPowerShellSingleQuotedString(expectedSha256)}
$expectedSizeBytes = {expectedSizeBytes}
$advertisedVersion = {ToPowerShellSingleQuotedString(advertisedVersion)}
$backupCreated = $false

function Write-UpdateLog([string]$message) {{
    Add-Content -LiteralPath $log -Value ""[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')] $message"" -Encoding UTF8
}}

try {{
    Set-Content -LiteralPath $log -Value ""[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')] Updating HonestFlow..."" -Encoding UTF8
    Start-Sleep -Seconds 2
    while (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) {{ Start-Sleep -Seconds 1 }}
    if (-not (Test-Path -LiteralPath $newExe)) {{ throw ""Verified update file is missing"" }}
    if ($expectedSizeBytes -le 0 -or (Get-Item -LiteralPath $newExe).Length -ne $expectedSizeBytes) {{
        throw ""Verified update file has unexpected size""
    }}
    $actualSha256 = (Get-FileHash -LiteralPath $newExe -Algorithm SHA256).Hash
    if (-not [string]::Equals($actualSha256, $expectedSha256, [System.StringComparison]::OrdinalIgnoreCase)) {{
        throw ""Verified update file SHA-256 mismatch""
    }}
    Write-UpdateLog ""Verified update: version=$advertisedVersion sha256=$actualSha256""
    if (Test-Path -LiteralPath $targetExe) {{
        Copy-Item -LiteralPath $targetExe -Destination $backupExe -Force
        $backupCreated = $true
    }}
    Copy-Item -LiteralPath $newExe -Destination $targetExe -Force
    if ((Get-Item -LiteralPath $targetExe).Length -ne $expectedSizeBytes) {{
        throw ""Installed update has unexpected size""
    }}
    $installedSha256 = (Get-FileHash -LiteralPath $targetExe -Algorithm SHA256).Hash
    if (-not [string]::Equals($installedSha256, $expectedSha256, [System.StringComparison]::OrdinalIgnoreCase)) {{
        throw ""Installed update SHA-256 mismatch""
    }}
    Start-Process -FilePath $targetExe -WorkingDirectory $appDir
    exit 0
}}
catch {{
    Write-UpdateLog ""Update failed: $($_.Exception.Message)""
    try {{
        if ($backupCreated -and (Test-Path -LiteralPath $backupExe)) {{
            Copy-Item -LiteralPath $backupExe -Destination $targetExe -Force
        }} elseif (Test-Path -LiteralPath $targetExe) {{
            Remove-Item -LiteralPath $targetExe -Force
        }}
    }} catch {{ Write-UpdateLog ""Backup restore failed: $($_.Exception.Message)"" }}
    try {{
        if (Test-Path -LiteralPath $targetExe) {{ Start-Process -FilePath $targetExe -WorkingDirectory $appDir }}
        else {{ Start-Process -FilePath $runningExe -WorkingDirectory $appDir }}
    }} catch {{ Write-UpdateLog ""Restart failed: $($_.Exception.Message)"" }}
    exit 1
}}
";
        }

        private static string ToPowerShellSingleQuotedString(string value) =>
            "'" + (value ?? string.Empty).Replace("'", "''") + "'";

        private static Version GetExecutableVersion(string exePath)
        {
            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(exePath);
            string versionText = versionInfo.FileVersion ?? versionInfo.ProductVersion;
            return Version.TryParse(versionText, out Version version) ? version : new Version(0, 0, 0, 0);
        }

        private static Version NormalizeVersion(string tag)
        {
            string cleaned = tag?.Trim().TrimStart('v', 'V') ?? "0.0.0.0";
            return Version.TryParse(cleaned, out Version version) ? version : new Version(0, 0, 0, 0);
        }
    }
}
