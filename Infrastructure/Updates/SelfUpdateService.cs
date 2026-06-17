using HonestFlow.Infrastructure.Configuration;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Dialogs;

namespace HonestFlow.Infrastructure.Updates
{
    public class SelfUpdateService
    {
        private const string Owner = "1Yoda1";
        private const string Repo = "HonestFlow";
        private const string UserAgent = "HonestFlow-Updater/1.0";
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
                    Logger.Warning("Автообновление: asset HonestFlow.exe в latest-релизе не найден", nameof(SelfUpdateService));
                    return false;
                }

                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
                Version latestVersion = NormalizeVersion(latest.Version);

                Logger.Info($"Автообновление: текущая версия {currentVersion}, latest {latestVersion}", nameof(SelfUpdateService));

                if (latestVersion <= currentVersion)
                    return false;

                bool shouldUpdate = _dialogService.Confirm(
                    $"Доступна новая версия HonestFlow: {latestVersion}\n\nОбновить сейчас?",
                    "Обновление HonestFlow",
                    UserDialogIcon.Information);

                if (!shouldUpdate)
                    return false;

                string updateRoot = Path.Combine(AppPaths.ProgramDataFolder, "update");
                Directory.CreateDirectory(updateRoot);

                string newExePath = Path.Combine(updateRoot, "HonestFlow.new.exe");
                string backupPath = Path.Combine(updateRoot, "backup");

                if (File.Exists(newExePath))
                    File.Delete(newExePath);

                await DownloadFile(latest.DownloadUrl, newExePath);

                if (!File.Exists(newExePath) || new FileInfo(newExePath).Length == 0)
                {
                    Logger.Error("Автообновление: скачанный HonestFlow.exe пустой или не найден", nameof(SelfUpdateService));
                    _dialogService.ShowError("Обновление скачано некорректно.", "Ошибка обновления");
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
            using var client = CreateClient();

            string apiUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
            string json = await client.GetStringAsync(apiUrl);

            dynamic release = JsonConvert.DeserializeObject(json);
            string tag = release.tag_name;

            foreach (var asset in release.assets)
            {
                string name = asset.name;
                if (string.Equals(name, UpdateAssetName, StringComparison.OrdinalIgnoreCase))
                {
                    return new SelfUpdateInfo
                    {
                        Version = tag,
                        AssetName = name,
                        DownloadUrl = asset.browser_download_url
                    };
                }
            }

            return null;
        }

        private async Task DownloadFile(string url, string destinationPath)
        {
            using var client = CreateClient();
            client.Timeout = TimeSpan.FromMinutes(5);

            using var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            await using var input = await response.Content.ReadAsStreamAsync();
            await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

            await input.CopyToAsync(output);
        }

        private void CreateAndRunUpdateScript(string newExePath, string backupPath)
        {
            string currentExe = Environment.ProcessPath;
            string appDir = AppPaths.BaseFolder.TrimEnd('\\');
            int currentPid = Environment.ProcessId;

            string scriptPath = Path.Combine(AppPaths.ProgramDataFolder, "update", "apply_update.cmd");
            string backupExe = Path.Combine(backupPath, "HonestFlow.backup.exe");

            string script = $@"
@echo off
chcp 65001 > nul
echo Updating HonestFlow...

timeout /t 2 /nobreak > nul

:wait
tasklist /FI ""PID eq {currentPid}"" | find ""{currentPid}"" > nul
if not errorlevel 1 (
    timeout /t 1 /nobreak > nul
    goto wait
)

if not exist ""{backupPath}"" mkdir ""{backupPath}""

copy /Y ""{currentExe}"" ""{backupExe}""
if errorlevel 1 (
    start """" ""{currentExe}""
    exit /b 1
)

copy /Y ""{newExePath}"" ""{currentExe}""
if errorlevel 1 (
    copy /Y ""{backupExe}"" ""{currentExe}""
    start """" ""{currentExe}""
    exit /b 1
)

start """" ""{currentExe}""
exit /b 0
";

            File.WriteAllText(scriptPath, script, Encoding.UTF8);

            Process.Start(new ProcessStartInfo
            {
                FileName = scriptPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                Verb = "runas"
            });

            System.Windows.Forms.Application.Exit();
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            return client;
        }

        private static Version NormalizeVersion(string tag)
        {
            string cleaned = tag?.Trim().TrimStart('v', 'V') ?? "0.0.0.0";
            return Version.TryParse(cleaned, out var version) ? version : new Version(0, 0, 0, 0);
        }
    }
}
