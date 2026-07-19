using HonestFlow.Application.Core;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Infrastructure.Downloads;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.Prerequisites
{
    public sealed class DotNetDesktopRuntimeInstaller : IDotNetDesktopRuntimeInstaller
    {
        public const long MinimumFreeDiskBytes = 1L * 1024L * 1024L * 1024L;
        public const long PackageSize = 60053808;
        public const string PackageFileName = "windowsdesktop-runtime-10.0.10-win-x64.exe";
        public const string PackageSha256 =
            "E82FC901C8F52D716293B2BC0830CE0DD254A06268C457A19E8FC503560A84D1";
        public const string PackageSignerThumbprint =
            "BB793DB742624269BB5F4515BBE9A3DF418F588D";

        private const long MaximumPackageBytes = 100L * 1024L * 1024L;
        private static readonly Version MinimumVersion = new(10, 0, 10);
        private readonly ILogService _log;
        private readonly YandexDiskDownloader _downloader;

        public DotNetDesktopRuntimeInstaller(ILogService log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _downloader = new YandexDiskDownloader(AppPaths.DotNetRuntimeCacheFolder);
        }

        public async Task<DotNetRuntimeInstallResult> EnsureInstalledAsync(
            IProgress<DotNetRuntimeInstallProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Report(progress, 2, "Проверяем наличие .NET Desktop Runtime 10...");
                string frameworkFolder = GetWindowsDesktopFrameworkFolder();
                if (IsRequiredRuntimeInstalled(frameworkFolder, MinimumVersion))
                {
                    return Success(
                        DotNetRuntimeInstallStatus.AlreadyInstalled,
                        ".NET Desktop Runtime 10 уже установлен.");
                }

                if (!Environment.Is64BitOperatingSystem)
                {
                    return Fail(
                        DotNetRuntimeInstallStatus.UnsupportedArchitecture,
                        "Автоматическая установка .NET 10 подготовлена только для Windows x64.");
                }

                long availableBytes = GetSystemDriveAvailableBytes();
                if (!HasSufficientFreeSpace(availableBytes))
                {
                    return new DotNetRuntimeInstallResult
                    {
                        Status = DotNetRuntimeInstallStatus.InsufficientDiskSpace,
                        AvailableDiskBytes = availableBytes,
                        Message = $"Для установки .NET 10 требуется не менее 1 ГБ свободного места. " +
                                  $"Доступно: {FormatBytes(availableBytes)}."
                    };
                }

                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(AppPaths.DotNetRuntimeCacheFolder);
                string packagePath = Path.Combine(
                    AppPaths.DotNetRuntimeCacheFolder,
                    PackageFileName);
                string validationError = ValidatePackage(packagePath);
                if (validationError != null)
                {
                    TryDelete(packagePath);
                    Report(progress, 5, "Ищем установщик .NET 10 на Яндекс.Диске...");
                    var assets = await _downloader.GetReleaseAssets(forceRefresh: true);
                    if (!assets.TryGetValue(PackageFileName, out var asset))
                    {
                        return Fail(
                            DotNetRuntimeInstallStatus.AssetNotFound,
                            $"На Яндекс.Диске не найден {PackageFileName}.");
                    }

                    if (asset.Size != PackageSize || asset.Size > MaximumPackageBytes)
                    {
                        return Fail(
                            DotNetRuntimeInstallStatus.InvalidPackage,
                            "Размер удалённого установщика .NET 10 не соответствует проверенной версии.");
                    }

                    var downloadProgress = new Progress<int>(percent =>
                        Report(
                            progress,
                            8 + Math.Clamp(percent, 0, 100) * 62 / 100,
                            $"Получаем .NET Desktop Runtime 10: {percent}%"));
                    bool downloaded = await _downloader.DownloadFileWithRetry(
                        asset.Url,
                        packagePath,
                        downloadProgress,
                        PackageSize,
                        maximumBytes: MaximumPackageBytes,
                        cancellationToken: cancellationToken);
                    if (!downloaded)
                    {
                        return Fail(
                            DotNetRuntimeInstallStatus.DownloadFailed,
                            "Не удалось получить установщик .NET 10 с Яндекс.Диска.");
                    }
                }
                else
                {
                    Report(progress, 70, "Используем проверенный кеш установщика .NET 10.");
                }

                Report(progress, 75, "Проверяем подпись установщика .NET 10...");
                validationError = ValidatePackage(packagePath);
                if (validationError != null)
                {
                    TryDelete(packagePath);
                    return Fail(DotNetRuntimeInstallStatus.InvalidPackage, validationError);
                }

                Report(progress, 82, "Устанавливаем .NET Desktop Runtime 10...");
                int exitCode = await RunInstallerAsync(packagePath, cancellationToken);
                if (exitCode != 0 && exitCode != 3010)
                {
                    return Fail(
                        DotNetRuntimeInstallStatus.InstallationFailed,
                        $"Установщик .NET 10 завершился с кодом {exitCode}.",
                        exitCode);
                }

                if (!IsRequiredRuntimeInstalled(frameworkFolder, MinimumVersion) && exitCode == 0)
                {
                    return Fail(
                        DotNetRuntimeInstallStatus.InstallationFailed,
                        ".NET 10 не обнаружен после завершения установщика.",
                        exitCode);
                }

                if (exitCode == 3010)
                {
                    Report(progress, 100, ".NET 10 установлен; потребуется перезагрузка Windows.");
                    return Success(
                        DotNetRuntimeInstallStatus.RebootRequired,
                        ".NET Desktop Runtime 10 установлен. Для завершения потребуется перезагрузка.",
                        exitCode);
                }

                Report(progress, 100, ".NET Desktop Runtime 10 установлен.");
                return Success(
                    DotNetRuntimeInstallStatus.Success,
                    ".NET Desktop Runtime 10 успешно установлен.",
                    exitCode);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogDebug($".NET 10 prerequisite error: {ex.GetType().Name}: {ex.Message}");
                return Fail(
                    DotNetRuntimeInstallStatus.UnexpectedError,
                    "Не удалось подготовить .NET Desktop Runtime 10.");
            }
        }

        public static bool IsRequiredRuntimeInstalled(
            string frameworkFolder,
            Version minimumVersion)
        {
            if (string.IsNullOrWhiteSpace(frameworkFolder) ||
                minimumVersion == null ||
                !Directory.Exists(frameworkFolder))
            {
                return false;
            }

            foreach (string directory in Directory.EnumerateDirectories(frameworkFolder))
            {
                if (Version.TryParse(Path.GetFileName(directory), out Version version) &&
                    version.Major == 10 &&
                    version >= minimumVersion)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasSufficientFreeSpace(long availableBytes) =>
            availableBytes >= MinimumFreeDiskBytes;

        public static string ValidatePayload(string path)
        {
            if (!File.Exists(path))
                return "Установщик .NET 10 отсутствует в кеше.";

            var file = new FileInfo(path);
            if (file.Length != PackageSize)
                return "Размер установщика .NET 10 не соответствует ожидаемому.";

            using var stream = File.OpenRead(path);
            using var sha256 = SHA256.Create();
            string actualHash = Convert.ToHexString(sha256.ComputeHash(stream));
            return string.Equals(actualHash, PackageSha256, StringComparison.OrdinalIgnoreCase)
                ? null
                : "SHA-256 установщика .NET 10 не соответствует ожидаемому.";
        }

        public static bool IsSuccessfulExitCode(int exitCode) =>
            exitCode == 0 || exitCode == 3010;

        private static string ValidatePackage(string path)
        {
            string payloadError = ValidatePayload(path);
            if (payloadError != null)
                return payloadError;

            try
            {
                using var certificate = new X509Certificate2(
                    X509Certificate.CreateFromSignedFile(path));
                string thumbprint = certificate.Thumbprint?.Replace(" ", string.Empty);
                return string.Equals(
                    thumbprint,
                    PackageSignerThumbprint,
                    StringComparison.OrdinalIgnoreCase)
                    ? null
                    : "Цифровая подпись установщика .NET 10 не соответствует Microsoft.";
            }
            catch
            {
                return "Не удалось проверить цифровую подпись установщика .NET 10.";
            }
        }

        private static string GetWindowsDesktopFrameworkFolder()
        {
            string programFiles = Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles);
            return Path.Combine(
                programFiles,
                "dotnet",
                "shared",
                "Microsoft.WindowsDesktop.App");
        }

        private static long GetSystemDriveAvailableBytes()
        {
            string systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
            return new DriveInfo(systemRoot).AvailableFreeSpace;
        }

        private static async Task<int> RunInstallerAsync(
            string packagePath,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = packagePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("/install");
            startInfo.ArgumentList.Add("/quiet");
            startInfo.ArgumentList.Add("/norestart");

            using Process process = Process.Start(startInfo) ??
                throw new InvalidOperationException("Не удалось запустить установщик .NET 10.");
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }

        private DotNetRuntimeInstallResult Success(
            DotNetRuntimeInstallStatus status,
            string message,
            int? exitCode = null)
        {
            _log.LogDebug($".NET 10 prerequisite: {status}");
            return new DotNetRuntimeInstallResult
            {
                Status = status,
                Message = message,
                ExitCode = exitCode
            };
        }

        private DotNetRuntimeInstallResult Fail(
            DotNetRuntimeInstallStatus status,
            string message,
            int? exitCode = null)
        {
            _log.LogDebug($".NET 10 prerequisite failed: {status}; ExitCode={exitCode}");
            return new DotNetRuntimeInstallResult
            {
                Status = status,
                Message = message,
                ExitCode = exitCode
            };
        }

        private static string FormatBytes(long bytes) =>
            bytes < 0 ? "не удалось определить" : $"{bytes / 1024d / 1024d / 1024d:F2} ГБ";

        private static void Report(
            IProgress<DotNetRuntimeInstallProgress> progress,
            int percent,
            string message) =>
            progress?.Report(new DotNetRuntimeInstallProgress(
                Math.Clamp(percent, 0, 100),
                message));

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Invalid package is never executed; cleanup can be retried later.
            }
        }
    }
}
