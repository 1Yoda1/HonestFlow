using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Application.Core;
using Microsoft.Win32;

namespace HonestFlow.Application.Diagnostics
{
    public class DiagnosticArchiveService
    {
        private readonly ILogService _log;
        private readonly List<string> _foundLogs = new();
        private readonly List<string> _missingLogs = new();
        private readonly List<string> _copyErrors = new();
        private string _fiscalAddress;

        public DiagnosticArchiveService(ILogService log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public string CreateArchive()
        {
            return CreateArchiveInfo().ArchivePath;
        }

        public DiagnosticArchiveInfo CreateArchiveInfo()
        {
            return CreateArchiveInfo(DiagnosticLogSelection.Full());
        }

        public DiagnosticArchiveInfo CreateArchiveInfo(DiagnosticLogSelection selection)
        {
            _foundLogs.Clear();
            _missingLogs.Clear();
            _copyErrors.Clear();
            _fiscalAddress = null;
            selection ??= DiagnosticLogSelection.Full();

            DateTime collectedAt = DateTime.Now;
            DateTime logCutoff = collectedAt.AddDays(-2);
            string archiveName = $"HF_Diagnostics_{collectedAt:yyyy-MM-dd_HH-mm-ss}.zip";
            string diagnosticsFolder = AppPaths.DiagnosticsFolder;
            string tempRoot = Path.Combine(diagnosticsFolder, "Temp", Path.GetFileNameWithoutExtension(archiveName));
            string archivePath = Path.Combine(diagnosticsFolder, archiveName);

            _log.LogUser("Начало сбора диагностики");

            Directory.CreateDirectory(diagnosticsFolder);
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
            Directory.CreateDirectory(tempRoot);

            try
            {
                if (selection.IncludeHonestFlow)
                    CollectHonestFlowLogs(tempRoot, logCutoff);

                if (selection.IncludeLm)
                    CollectLmLogs(tempRoot, logCutoff);

                if (selection.IncludeEsm)
                    CollectEsmLog(tempRoot, logCutoff);

                if (selection.IncludeKkt)
                    CollectAtolLog(tempRoot, logCutoff);

                if (selection.IncludeSystemInfo)
                    WriteSystemInfo(tempRoot, collectedAt);

                if (File.Exists(archivePath))
                    File.Delete(archivePath);

                ZipFile.CreateFromDirectory(tempRoot, archivePath, CompressionLevel.Optimal, false);
                _log.LogUser($"Диагностический архив создан: {archivePath}");

                return new DiagnosticArchiveInfo(archivePath, _fiscalAddress);
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private void CollectHonestFlowLogs(string tempRoot, DateTime cutoff)
        {
            string logsFolder = Logger.GetLogsFolder();
            string targetFolder = Path.Combine(tempRoot, "Diagnostics", "HonestFlow");

            if (!Directory.Exists(logsFolder))
            {
                AddMissing($"HonestFlow logs folder: {logsFolder}");
                return;
            }

            var files = Directory.GetFiles(logsFolder, "*.log")
                .OrderByDescending(File.GetLastWriteTime)
                .Take(6)
                .ToArray();
            if (files.Length == 0)
                AddMissing($"HonestFlow *.log: {logsFolder}");

            foreach (string file in files)
                TryCopyRecentLog(file, Path.Combine(targetFolder, Path.GetFileName(file)), "HonestFlow", cutoff);
        }

        private void CollectLmLogs(string tempRoot, DateTime cutoff)
        {
            string regimeLogFolder = FindRegimeLogFolder();
            string lmControllerLogFolder = @"C:\ProgramData\ESP\lmcontroller\log";
            string targetFolder = Path.Combine(tempRoot, "Diagnostics", "LM");

            if (string.IsNullOrWhiteSpace(regimeLogFolder))
            {
                AddMissing("Regime log folder: не удалось определить путь");
            }
            else
            {
                TryCopyKnownLog(
                    Path.Combine(regimeLogFolder, "regime.log"),
                    Path.Combine(targetFolder, "regime.log"),
                    "LM",
                    cutoff);

                TryCopyKnownLog(
                    Path.Combine(regimeLogFolder, "yenisei.log"),
                    Path.Combine(targetFolder, "yenisei.log"),
                    "LM",
                    cutoff);
            }

            TryCopyKnownLog(
                Path.Combine(lmControllerLogFolder, "lmcontroller.log"),
                Path.Combine(targetFolder, "lmcontroller.log"),
                "LM");
        }

        private void CollectEsmLog(string tempRoot, DateTime cutoff)
        {
            string esmLogFolder = @"C:\ProgramData\ESP\ESM\um\log";
            string targetFolder = Path.Combine(tempRoot, "Diagnostics", "ESM");

            if (!Directory.Exists(esmLogFolder))
            {
                AddMissing($"ESM log folder: {esmLogFolder}");
                return;
            }

            TryCopyKnownLog(
                Path.Combine(esmLogFolder, "esm-orchestrator.log"),
                Path.Combine(targetFolder, "esm-orchestrator.log"),
                "ESM",
                cutoff);

            string esmLog = Directory.GetFiles(esmLogFolder, "esm-cm_*.log")
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(esmLog))
            {
                AddMissing($"ESM esm-cm_*.log: {esmLogFolder}");
                return;
            }

            TryCopyRecentLog(esmLog, Path.Combine(targetFolder, Path.GetFileName(esmLog)), "ESM", cutoff);
        }

        private void CollectAtolLog(string tempRoot, DateTime cutoff)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string atolLogPath = Path.Combine(appData, "ATOL", "drivers10", "logs", "fptr10.log");
            string targetFolder = Path.Combine(tempRoot, "Diagnostics", "KKT");

            TryCopyKnownLog(
                atolLogPath,
                Path.Combine(targetFolder, "fptr10.log"),
                "KKT",
                cutoff,
                CaptureFiscalAddress);
        }

        private void TryCopyKnownLog(string sourcePath, string targetPath, string group, DateTime cutoff, Action<string> acceptedLine = null)
        {
            if (!File.Exists(sourcePath))
            {
                AddMissing(sourcePath);
                return;
            }

            TryCopyRecentLog(sourcePath, targetPath, group, cutoff, acceptedLine);
        }

        private void TryCopyKnownLog(string sourcePath, string targetPath, string group)
        {
            if (!File.Exists(sourcePath))
            {
                AddMissing(sourcePath);
                return;
            }

            TryCopyFile(sourcePath, targetPath, group);
        }

        private string FindRegimeLogFolder()
        {
            foreach (string baseFolder in GetRegimeBaseFolderCandidates().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(baseFolder))
                    continue;

                string logFolder = Path.Combine(baseFolder, "var", "log");
                _log.LogDebug($"Диагностика: проверка папки логов Regime: {logFolder}");

                if (Directory.Exists(logFolder))
                    return logFolder;
            }

            return null;
        }

        private IEnumerable<string> GetRegimeBaseFolderCandidates()
        {
            string serviceImagePath = TryReadRegistryValue(
                Registry.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Regime",
                "ImagePath");

            string serviceBaseFolder = TryGetBaseFolderFromExecutablePath(serviceImagePath);
            if (!string.IsNullOrWhiteSpace(serviceBaseFolder))
                yield return serviceBaseFolder;

            foreach (string folder in GetRegimeBaseFoldersFromUninstallRegistry())
                yield return folder;

            yield return @"F:\Program Files\Regime";
            yield return @"C:\Program Files\Regime";
            yield return @"C:\Program Files (x86)\Regime";
        }

        private static IEnumerable<string> GetRegimeBaseFoldersFromUninstallRegistry()
        {
            string[] uninstallRoots =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (string uninstallRoot in uninstallRoots)
            {
                using var root = Registry.LocalMachine.OpenSubKey(uninstallRoot);
                if (root == null)
                    continue;

                foreach (string subKeyName in root.GetSubKeyNames())
                {
                    using var subKey = root.OpenSubKey(subKeyName);
                    string displayName = subKey?.GetValue("DisplayName")?.ToString() ?? string.Empty;

                    if (!displayName.Contains("Локальный модуль", StringComparison.OrdinalIgnoreCase) &&
                        !displayName.Contains("Regime", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string installLocation = subKey?.GetValue("InstallLocation")?.ToString();
                    if (!string.IsNullOrWhiteSpace(installLocation))
                        yield return installLocation.Trim('"');

                    string displayIcon = subKey?.GetValue("DisplayIcon")?.ToString();
                    string displayIconFolder = TryGetBaseFolderFromExecutablePath(displayIcon);
                    if (!string.IsNullOrWhiteSpace(displayIconFolder))
                        yield return displayIconFolder;

                    string uninstallString = subKey?.GetValue("UninstallString")?.ToString();
                    string uninstallFolder = TryGetBaseFolderFromExecutablePath(uninstallString);
                    if (!string.IsNullOrWhiteSpace(uninstallFolder))
                        yield return uninstallFolder;
                }
            }
        }

        private static string TryReadRegistryValue(RegistryKey root, string subKeyPath, string valueName)
        {
            try
            {
                using var key = root.OpenSubKey(subKeyPath);
                return key?.GetValue(valueName)?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string TryGetBaseFolderFromExecutablePath(string rawPath)
        {
            string executablePath = TryExtractExecutablePath(rawPath);
            if (string.IsNullOrWhiteSpace(executablePath))
                return null;

            string directory = Path.GetDirectoryName(executablePath);
            if (string.IsNullOrWhiteSpace(directory))
                return null;

            var current = new DirectoryInfo(directory);
            while (current != null)
            {
                if (string.Equals(current.Name, "Regime", StringComparison.OrdinalIgnoreCase))
                    return current.FullName;

                current = current.Parent;
            }

            return directory;
        }

        private static string TryExtractExecutablePath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return null;

            string value = rawPath.Trim();
            if (value.StartsWith("\"", StringComparison.Ordinal))
            {
                int endQuote = value.IndexOf('"', 1);
                if (endQuote > 1)
                    return Environment.ExpandEnvironmentVariables(value.Substring(1, endQuote - 1));
            }

            int exeIndex = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIndex >= 0)
                return Environment.ExpandEnvironmentVariables(value.Substring(0, exeIndex + 4).Trim('"'));

            return Environment.ExpandEnvironmentVariables(value.Trim('"'));
        }

        private void TryCopyFile(string sourcePath, string targetPath, string group)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

                using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
                source.CopyTo(target);

                string entry = $"{group}: {sourcePath}";
                _foundLogs.Add(entry);
                _log.LogDebug($"Диагностика: найден и скопирован файл: {sourcePath}");
            }
            catch (Exception ex)
            {
                string message = $"{sourcePath}: {ex.Message}";
                _copyErrors.Add(message);
                _log.LogDebug($"Диагностика: ошибка копирования {message}");
            }
        }

        private void TryCopyRecentLog(string sourcePath, string targetPath, string group, DateTime cutoff, Action<string> acceptedLine = null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

                int written = 0;
                bool includeContinuation = false;
                using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                using var target = new StreamWriter(targetPath, false, new UTF8Encoding(false));

                target.WriteLine($"Log trimmed by HonestFlow diagnostics. Cutoff: {cutoff:yyyy-MM-dd HH:mm:ss}");
                target.WriteLine($"Source: {sourcePath}");
                target.WriteLine();

                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    DateTime? timestamp = TryParseLogTimestamp(line);
                    bool include = timestamp.HasValue ? timestamp.Value >= cutoff : includeContinuation;

                    if (!include)
                    {
                        if (timestamp.HasValue)
                            includeContinuation = false;
                        continue;
                    }

                    target.WriteLine(line);
                    acceptedLine?.Invoke(line);
                    written++;

                    if (timestamp.HasValue)
                        includeContinuation = true;
                }

                if (written == 0)
                    target.WriteLine("No log lines found for the last 2 days.");

                string entry = $"{group}: {sourcePath} (last 2 days, lines: {written})";
                _foundLogs.Add(entry);
                _log.LogDebug($"Диагностика: добавлен срез лога за 2 суток: {sourcePath}, строк: {written}");
            }
            catch (Exception ex)
            {
                string message = $"{sourcePath}: {ex.Message}";
                _copyErrors.Add(message);
                _log.LogDebug($"Диагностика: ошибка обработки лога {message}");
            }
        }

        private static DateTime? TryParseLogTimestamp(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            Match bracket = Regex.Match(line, @"\[(?<date>\d{4}-\d{2}-\d{2}) (?<time>\d{2}:\d{2}:\d{2})");
            if (bracket.Success &&
                DateTime.TryParse($"{bracket.Groups["date"].Value} {bracket.Groups["time"].Value}", out var bracketTime))
            {
                return bracketTime;
            }

            Match iso = Regex.Match(line, @"(?:^|\s)(?<stamp>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z)");
            if (iso.Success &&
                DateTime.TryParse(
                    iso.Groups["stamp"].Value,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var isoTime))
            {
                return isoTime.ToLocalTime();
            }

            Match dotted = Regex.Match(line, @"^(?<date>\d{4}\.\d{2}\.\d{2})\s+(?<time>\d{2}:\d{2}:\d{2})");
            if (dotted.Success &&
                DateTime.TryParseExact(
                    $"{dotted.Groups["date"].Value} {dotted.Groups["time"].Value}",
                    "yyyy.MM.dd HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var dottedTime))
            {
                return dottedTime;
            }

            return null;
        }

        private void CaptureFiscalAddress(string line)
        {
            string address = TryExtractFiscalAddress(line);
            if (!string.IsNullOrWhiteSpace(address))
                _fiscalAddress = address;
        }

        private static string TryExtractFiscalAddress(string line)
        {
            if (string.IsNullOrWhiteSpace(line) ||
                line.IndexOf("address", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            Match escaped = Regex.Match(line, @"\\\""address\\\""\s*:\s*\\\""(?<address>[^\\\""]+)\\\""");
            if (escaped.Success)
                return escaped.Groups["address"].Value.Trim();

            Match plain = Regex.Match(line, @"""address""\s*:\s*""(?<address>[^""]+)""");
            if (plain.Success)
                return plain.Groups["address"].Value.Trim();

            return null;
        }

        private void WriteSystemInfo(string tempRoot, DateTime collectedAt)
        {
            var sb = new StringBuilder();

            sb.AppendLine("HonestFlow diagnostics");
            sb.AppendLine("======================");
            sb.AppendLine($"Дата и время сбора диагностики: {collectedAt:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Имя компьютера: {Environment.MachineName}");
            sb.AppendLine($"Пользователь Windows: {Environment.UserName}");
            sb.AppendLine($"Версия ОС Windows: {Environment.OSVersion}");
            sb.AppendLine($"Версия HonestFlow: {Assembly.GetExecutingAssembly().GetName().Version}");
            sb.AppendLine($"Рабочая папка приложения: {AppPaths.BaseFolder}");
            sb.AppendLine($"Свободное место на диске C: {GetDriveFreeSpace("C")}");
            sb.AppendLine($"Адрес из лога ККТ: {ValueOrFallback(_fiscalAddress, "не найден")}");
            sb.AppendLine();

            sb.AppendLine("Статусы служб:");
            AppendServiceStatus(sb, "regime");
            AppendServiceStatus(sb, "yenisei");
            AppendServiceStatus(sb, "lmcontroller");
            sb.AppendLine();

            AppendList(sb, "Найденные логи:", _foundLogs);
            AppendList(sb, "Не найденные логи:", _missingLogs);
            AppendList(sb, "Ошибки копирования файлов:", _copyErrors);

            string diagnosticsRoot = Path.Combine(tempRoot, "Diagnostics");
            Directory.CreateDirectory(diagnosticsRoot);
            File.WriteAllText(Path.Combine(diagnosticsRoot, "system_info.txt"), sb.ToString(), Encoding.UTF8);
        }

        private static void AppendServiceStatus(StringBuilder sb, string serviceName)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "sc",
                    Arguments = $"query {serviceName}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    sb.AppendLine($"- {serviceName}: Not Installed / Stopped");
                    return;
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                string combined = $"{output}\n{error}";
                if (process.ExitCode != 0 ||
                    combined.Contains("FAILED 1060", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"- {serviceName}: Not Installed / Stopped");
                    return;
                }

                string runningState = output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)
                    ? "Running"
                    : "Stopped";

                sb.AppendLine($"- {serviceName}: Installed / {runningState}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"- {serviceName}: статус не удалось получить ({ex.Message})");
            }
        }

        private static string ValueOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string GetDriveFreeSpace(string driveName)
        {
            try
            {
                var drive = new DriveInfo(driveName);
                if (!drive.IsReady)
                    return "диск недоступен";

                return $"{drive.AvailableFreeSpace / 1024 / 1024 / 1024} ГБ";
            }
            catch (Exception ex)
            {
                return $"не удалось получить ({ex.Message})";
            }
        }

        private static void AppendList(StringBuilder sb, string title, List<string> values)
        {
            sb.AppendLine(title);
            if (values.Count == 0)
            {
                sb.AppendLine("- нет");
                sb.AppendLine();
                return;
            }

            foreach (string value in values)
                sb.AppendLine($"- {value}");
            sb.AppendLine();
        }

        private void AddMissing(string path)
        {
            _missingLogs.Add(path);
            _log.LogDebug($"Диагностика: файл не найден: {path}");
        }

        private void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch (Exception ex)
            {
                _log.LogDebug($"Диагностика: не удалось удалить временную папку {path}: {ex.Message}");
            }
        }
    }
}
