using HonestFlow.Application.Core;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Infrastructure.Downloads;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.RemoteAccess
{
    public sealed class RuDesktopInstaller : IRuDesktopInstaller
    {
        private const long MaximumPackageSize = 100L * 1024L * 1024L;
        private const string ExpectedSignerThumbprint = "8365B69A1A2077E392A98F3513DDD5795EF8B5FC";

        private static readonly RuDesktopPackage X64Package = new()
        {
            Version = "3.0.1563",
            FileName = "rudesktop-3.0.1563-x64.msi",
            Size = 31748096,
            Sha256 = "02F6DCE059EAE49F7399F6077EBAC032B63224D0D898306D96727700E9E01FEE",
            SignerThumbprint = ExpectedSignerThumbprint
        };

        private static readonly RuDesktopPackage X86Package = new()
        {
            Version = "3.0.1563",
            FileName = "rudesktop-3.0.1563-x32.msi",
            Size = 22806528,
            Sha256 = "BBF0C5EC01810A5E0363484FA40F61742774EEAA4FE7F7D8CB59DA8B9635B429",
            SignerThumbprint = ExpectedSignerThumbprint
        };

        private readonly ILogService _log;
        private readonly YandexDiskDownloader _downloader;

        public RuDesktopInstaller(ILogService log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _downloader = new YandexDiskDownloader(AppPaths.RuDesktopInstallerCacheFolder);
        }

        public async Task<RuDesktopInstallResult> InstallAsync(
            IProgress<RuDesktopInstallProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            RuDesktopPackage package = GetPackageForCurrentOperatingSystem();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(AppPaths.RuDesktopInstallerCacheFolder);
                Directory.CreateDirectory(AppPaths.LogsFolder);
                string packagePath = Path.Combine(AppPaths.RuDesktopInstallerCacheFolder, package.FileName);

                bool packageIsValid = File.Exists(packagePath) &&
                                      string.IsNullOrWhiteSpace(ValidatePackage(packagePath, package));
                if (!packageIsValid)
                {
                    if (File.Exists(packagePath))
                        File.Delete(packagePath);

                    Report(progress, 2, $"Поиск {package.FileName} на Яндекс.Диске...");
                    var assets = await _downloader.GetReleaseAssets(forceRefresh: true);
                    if (!assets.TryGetValue(package.FileName, out var asset))
                    {
                        return Fail(
                            RuDesktopInstallStatus.AssetNotFound,
                            $"На Яндекс.Диске не найден {package.FileName}.");
                    }

                    if (asset.Size <= 0 || asset.Size > MaximumPackageSize || asset.Size != package.Size)
                    {
                        return Fail(
                            RuDesktopInstallStatus.InvalidPackage,
                            "Размер удалённого установщика RuDesktop не соответствует проверенной версии.");
                    }

                    var downloadProgress = new Progress<int>(percent =>
                        Report(progress, 5 + Math.Clamp(percent, 0, 100) * 65 / 100,
                            $"Получение RuDesktop: {percent}%"));

                    bool downloaded = await _downloader.DownloadFileWithRetry(
                        asset.Url,
                        packagePath,
                        downloadProgress,
                        package.Size,
                        maximumBytes: MaximumPackageSize,
                        cancellationToken: cancellationToken);
                    if (!downloaded)
                    {
                        return Fail(
                            RuDesktopInstallStatus.DownloadFailed,
                            "Не удалось получить установщик RuDesktop с Яндекс.Диска.");
                    }
                }
                else
                {
                    Report(progress, 70, "Используется проверенный кеш RuDesktop.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, 75, "Проверка установщика RuDesktop...");
                string validationError = ValidatePackage(packagePath, package);
                if (!string.IsNullOrWhiteSpace(validationError))
                {
                    TryDeleteInvalidPackage(packagePath);
                    return Fail(RuDesktopInstallStatus.InvalidPackage, validationError);
                }

                Report(progress, 85, "Установка RuDesktop...");
                int exitCode = await RunMsiAsync(packagePath);
                if (exitCode == 0)
                {
                    Report(progress, 100, "RuDesktop установлен.");
                    _log.LogUser($"RuDesktop {package.Version} установлен успешно");
                    return new RuDesktopInstallResult
                    {
                        Status = RuDesktopInstallStatus.Success,
                        ExitCode = exitCode,
                        Message = $"RuDesktop {package.Version} установлен."
                    };
                }

                if (exitCode == 3010)
                {
                    Report(progress, 100, "RuDesktop установлен; требуется перезагрузка.");
                    _log.LogUser($"RuDesktop {package.Version} установлен; требуется перезагрузка");
                    return new RuDesktopInstallResult
                    {
                        Status = RuDesktopInstallStatus.RebootRequired,
                        ExitCode = exitCode,
                        Message = $"RuDesktop {package.Version} установлен. Для завершения требуется перезагрузка Windows."
                    };
                }

                return Fail(
                    RuDesktopInstallStatus.InstallationFailed,
                    $"Установщик RuDesktop завершился с кодом {exitCode}.",
                    exitCode);
            }
            catch (OperationCanceledException)
            {
                return Fail(RuDesktopInstallStatus.UserCancelled, "Установка RuDesktop отменена.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return Fail(RuDesktopInstallStatus.UserCancelled, "Запрос прав администратора был отменён.");
            }
            catch (Exception ex)
            {
                _log.LogDebug($"RuDesktop installer error: {ex.Message}");
                return Fail(
                    RuDesktopInstallStatus.UnexpectedError,
                    $"Не удалось установить RuDesktop: {ex.Message}");
            }
        }

        public static RuDesktopPackage GetPackageForOperatingSystem(bool is64BitOperatingSystem)
        {
            return is64BitOperatingSystem ? X64Package : X86Package;
        }

        public static RuDesktopPackage GetPackageForCurrentOperatingSystem()
        {
            return GetPackageForOperatingSystem(Environment.Is64BitOperatingSystem);
        }

        public static bool IsSuccessfulExitCode(int exitCode)
        {
            return exitCode == 0 || exitCode == 3010;
        }

        public static string ValidatePayload(string path, long expectedSize, string expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return "Установщик RuDesktop не найден в кеше.";

            var info = new FileInfo(path);
            if (info.Length != expectedSize)
                return "Размер установщика RuDesktop не совпадает с ожидаемым.";

            using var stream = File.OpenRead(path);
            using var sha256 = SHA256.Create();
            string actualHash = Convert.ToHexString(sha256.ComputeHash(stream));
            if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                return "SHA-256 установщика RuDesktop не совпадает с ожидаемым.";

            return null;
        }

        private static string ValidatePackage(string path, RuDesktopPackage package)
        {
            string payloadError = ValidatePayload(path, package.Size, package.Sha256);
            if (!string.IsNullOrWhiteSpace(payloadError))
                return payloadError;

            try
            {
                using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
                string actualThumbprint = certificate.Thumbprint?.Replace(" ", string.Empty);
                if (!string.Equals(actualThumbprint, package.SignerThumbprint, StringComparison.OrdinalIgnoreCase))
                    return "Издатель установщика RuDesktop не соответствует ожидаемому.";
            }
            catch (Exception)
            {
                return "Не удалось проверить цифровую подпись установщика RuDesktop.";
            }

            return null;
        }

        private static async Task<int> RunMsiAsync(string packagePath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("/i");
            startInfo.ArgumentList.Add(packagePath);
            startInfo.ArgumentList.Add("/qn");
            startInfo.ArgumentList.Add("/norestart");
            startInfo.ArgumentList.Add("ADDLOCAL=FeatureCore");
            startInfo.ArgumentList.Add("/L*v");
            startInfo.ArgumentList.Add(AppPaths.RuDesktopInstallerLogFile);

            using var process = Process.Start(startInfo);
            if (process == null)
                throw new InvalidOperationException("Не удалось запустить msiexec.exe.");

            await process.WaitForExitAsync();
            return process.ExitCode;
        }

        private RuDesktopInstallResult Fail(RuDesktopInstallStatus status, string message, int? exitCode = null)
        {
            _log.LogUser(message, isError: true);
            return new RuDesktopInstallResult
            {
                Status = status,
                ExitCode = exitCode,
                Message = message
            };
        }

        private static void Report(IProgress<RuDesktopInstallProgress> progress, int percent, string message)
        {
            progress?.Report(new RuDesktopInstallProgress(Math.Clamp(percent, 0, 100), message));
        }

        private static void TryDeleteInvalidPackage(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // The invalid package will never be executed; cleanup can be retried later.
            }
        }
    }
}
