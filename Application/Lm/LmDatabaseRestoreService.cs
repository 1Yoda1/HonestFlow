using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HonestFlow.Application.Core;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Infrastructure.Installers;
using HonestFlow.Models;
using Microsoft.Win32;

namespace HonestFlow.Application.Lm
{
    public class LmDatabaseRestoreService
    {
        private readonly ILogService _log;
        private readonly IProgressService _progress;
        private readonly IUserDialogService _dialogService;
        private readonly bool _useRemoteConfigMode;

        public LmDatabaseRestoreService(
            ILogService log,
            IProgressService progress,
            IUserDialogService dialogService,
            bool useRemoteConfigMode)
        {
            _log = log;
            _progress = progress;
            _dialogService = dialogService ?? new WinFormsDialogService();
            _useRemoteConfigMode = useRemoteConfigMode;
        }

        public async Task<bool> Restore(IPData selectedIP)
        {
            if (selectedIP == null)
                throw new ArgumentNullException(nameof(selectedIP));

            if (!selectedIP.HasLmDatabaseBackup)
            {
                string message = $"База ЛМ ЧЗ не найдена по вашему ИНН: {MaskInn(selectedIP.Inn)}.";
                _log.LogUser(message, true);
                _dialogService.ShowInformation(message, "Восстановление базы ЛМ ЧЗ");
                return false;
            }

            string normalizedInn = NormalizeInn(selectedIP.Inn);
            if (string.IsNullOrWhiteSpace(normalizedInn))
            {
                const string message = "Не удалось определить ИНН точки для поиска базы ЛМ ЧЗ.";
                _log.LogUser(message, true);
                _dialogService.ShowWarning(message, "Восстановление базы ЛМ ЧЗ");
                return false;
            }

            string installFolder = FindInstalledRegimeFolder();
            if (string.IsNullOrWhiteSpace(installFolder))
            {
                const string message = "Не удалось определить текущую папку ЛМ ЧЗ. Восстановление базы отменено.";
                _log.LogUser(message, true);
                _dialogService.ShowWarning(message, "Восстановление базы ЛМ ЧЗ");
                return false;
            }

            string installerPath = await ResolveLmInstallerPath(selectedIP);
            if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
            {
                const string message = "Не найден установщик ЛМ ЧЗ для восстановления базы.";
                _log.LogUser(message, true);
                _dialogService.ShowWarning(message, "Восстановление базы ЛМ ЧЗ");
                return false;
            }

            string archiveName = $"Regime_{normalizedInn}.zip";
            string archivePath = await ResolveDatabaseArchivePath(archiveName);
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            {
                string message = $"База ЛМ ЧЗ не найдена по вашему ИНН: {MaskInn(selectedIP.Inn)}.";
                _log.LogUser($"{message} Ожидался архив: {archiveName}", true);
                _dialogService.ShowInformation(message, "Восстановление базы ЛМ ЧЗ");
                return false;
            }

            bool confirmed = _dialogService.Confirm(
                "Будет выполнено восстановление базы ЛМ ЧЗ.\n\n" +
                "Что произойдёт:\n" +
                "1. Запустится удаление текущего ЛМ ЧЗ.\n" +
                "2. В окне удаления нужно нажать \"Да\" на вопрос удаления файлов баз данных и настроек.\n" +
                "3. HonestFlow распакует архив базы и установит ЛМ ЧЗ поверх неё.\n\n" +
                $"Папка ЛМ ЧЗ: {installFolder}\n" +
                $"Архив базы: {archiveName}\n\n" +
                "Продолжить?",
                "Восстановление базы ЛМ ЧЗ",
                UserDialogIcon.Warning);

            if (!confirmed)
                return false;

            try
            {
                _log.LogUser("=== ВОССТАНОВЛЕНИЕ БАЗЫ ЛМ ЧЗ ===");
                _log.LogDebug($"LM DB restore: inn={MaskInn(selectedIP.Inn)}, archive={archivePath}, installer={installerPath}, installFolder={installFolder}");

                _progress.SetProgress(10, "ЛМ ЧЗ: подготовка восстановления базы");
                _progress.SetProgress(20, "ЛМ ЧЗ: удаление текущего модуля");

                var installer = new LmModuleInstaller(installerPath, _dialogService);
                await installer.RestoreDatabaseFromArchive(archivePath, installFolder);

                _progress.SetProgress(100, "ЛМ ЧЗ: база восстановлена");
                _log.LogUser("База ЛМ ЧЗ восстановлена.");
                _dialogService.ShowInformation("База ЛМ ЧЗ восстановлена.", "Готово");
                return true;
            }
            catch (OperationCanceledException ex)
            {
                _progress.SetProgress(100, "ЛМ ЧЗ: восстановление базы отменено");
                _log.LogUser($"Восстановление базы ЛМ ЧЗ отменено: {ex.Message}", true);
                _log.LogDebug($"Восстановление базы ЛМ ЧЗ отменено: {ex}");
                _dialogService.ShowWarning(ex.Message, "Восстановление базы ЛМ ЧЗ");
                return false;
            }
            catch (Exception ex)
            {
                _progress.SetProgress(100, "ЛМ ЧЗ: ошибка восстановления базы");
                _log.LogUser($"Ошибка восстановления базы ЛМ ЧЗ: {ex.Message}", true);
                _log.LogDebug($"Ошибка восстановления базы ЛМ ЧЗ: {ex}");
                _dialogService.ShowError($"Ошибка восстановления базы ЛМ ЧЗ:\n{ex.Message}", "Ошибка");
                return false;
            }
        }

        private async Task<string> ResolveDatabaseArchivePath(string archiveName)
        {
            string localPath = Path.Combine(ConfigManager.GetInstallersFolder(), archiveName);
            if (File.Exists(localPath))
            {
                _log.LogDebug($"Архив базы ЛМ ЧЗ найден локально: {localPath}");
                return localPath;
            }

            _progress.SetProgress(15, $"Скачивание базы ЛМ ЧЗ: {archiveName}");
            var progress = new Progress<int>(percent =>
            {
                _progress.SetProgress(15 + percent * 20 / 100, $"Скачивание базы ЛМ ЧЗ: {percent}%");
            });

            bool downloaded = await ConfigManager.DownloadInstallerIfNeeded(archiveName, progress);
            if (!downloaded)
                return null;

            string remotePath = AppPaths.ResolveRemoteInstallerPath(archiveName);
            _log.LogDebug($"Архив базы ЛМ ЧЗ скачан: {remotePath}");
            return remotePath;
        }

        private async Task<string> ResolveLmInstallerPath(IPData selectedIP)
        {
            VersionsData versions = _useRemoteConfigMode
                ? ConfigManager.LoadRemoteVersions()
                : ConfigManager.LoadVersions();

            VersionsData effectiveVersions = ApplyClientVersionOverrides(selectedIP, versions);
            string lmVersion = effectiveVersions?.LmModule;
            if (string.IsNullOrWhiteSpace(lmVersion))
                return Directory.GetFiles(ConfigManager.GetInstallersFolder(), "regime-*.msi").FirstOrDefault();

            string fileName = $"regime-{lmVersion}.msi";

            if (_useRemoteConfigMode)
            {
                var progress = new Progress<int>(percent =>
                {
                    _progress.SetProgress(35 + percent * 15 / 100, $"Скачивание установщика ЛМ ЧЗ: {percent}%");
                });

                bool downloaded = await ConfigManager.DownloadInstallerIfNeeded(fileName, progress);
                if (!downloaded)
                    return null;

                return AppPaths.ResolveRemoteInstallerPath(fileName);
            }

            string installersFolder = ConfigManager.GetInstallersFolder();
            string exactPath = Path.Combine(installersFolder, fileName);
            if (File.Exists(exactPath))
                return exactPath;

            return Directory.GetFiles(installersFolder, "regime-*.msi").FirstOrDefault();
        }

        private static VersionsData ApplyClientVersionOverrides(IPData selectedIP, VersionsData globalVersions)
        {
            var result = new VersionsData
            {
                LmModule = globalVersions?.LmModule,
                AtolDriver = globalVersions?.AtolDriver,
                ESM = globalVersions?.ESM,
                Controller = globalVersions?.Controller,
                HonestFlow = globalVersions?.HonestFlow
            };

            if (selectedIP?.Versions == null)
                return result;

            if (!string.IsNullOrWhiteSpace(selectedIP.Versions.LmModule))
                result.LmModule = selectedIP.Versions.LmModule;

            return result;
        }

        private static string FindInstalledRegimeFolder()
        {
            string location = FindInstallLocationInRegistry(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (!string.IsNullOrWhiteSpace(location))
                return NormalizeInstallLocation(location);

            location = FindInstallLocationInRegistry(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
            if (!string.IsNullOrWhiteSpace(location))
                return NormalizeInstallLocation(location);

            return FindExistingRegimeFolderOnFixedDrives();
        }

        private static string FindInstallLocationInRegistry(string uninstallRoot)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(uninstallRoot);
                if (key == null)
                    return null;

                foreach (string subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    string displayName = subKey?.GetValue("DisplayName")?.ToString() ?? string.Empty;
                    if (!displayName.Contains("Локальный модуль", StringComparison.OrdinalIgnoreCase) &&
                        !displayName.Contains("Regime", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string installLocation = subKey?.GetValue("InstallLocation")?.ToString();
                    if (!string.IsNullOrWhiteSpace(installLocation))
                        return installLocation;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static string NormalizeInstallLocation(string path)
        {
            string fullPath = Path.GetFullPath(path);
            return string.Equals(Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), "Regime", StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : Path.Combine(fullPath, "Regime");
        }

        private static string FindExistingRegimeFolderOnFixedDrives()
        {
            try
            {
                foreach (var drive in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed && x.IsReady))
                {
                    string root = drive.RootDirectory.FullName;
                    string[] candidates =
                    {
                        Path.Combine(root, "Program Files", "Regime"),
                        Path.Combine(root, "Program Files (x86)", "Regime")
                    };

                    foreach (string candidate in candidates)
                    {
                        if (Directory.Exists(candidate))
                            return candidate;
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static string NormalizeInn(string inn)
        {
            if (string.IsNullOrWhiteSpace(inn))
                return string.Empty;

            return new string(inn.Where(char.IsDigit).ToArray());
        }

        private static string MaskInn(string inn)
        {
            if (string.IsNullOrWhiteSpace(inn) || inn.Length < 6)
                return inn ?? string.Empty;

            return inn.Substring(0, 4) + new string('*', Math.Max(0, inn.Length - 6)) + inn.Substring(inn.Length - 2);
        }
    }
}
