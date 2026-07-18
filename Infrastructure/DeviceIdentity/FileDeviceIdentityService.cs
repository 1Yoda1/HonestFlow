using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.DeviceIdentity;
using HonestFlow.Infrastructure.Configuration;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.DeviceIdentity
{
    public sealed class FileDeviceIdentityService : IDeviceIdentityService
    {
        private const string ModuleName = nameof(FileDeviceIdentityService);
        private readonly string _stateFilePath;
        private readonly IDeviceIdentityStateProtector _protector;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public FileDeviceIdentityService(IDeviceIdentityStateProtector protector)
            : this(AppPaths.DeviceIdentityFile, protector)
        {
        }

        public FileDeviceIdentityService(
            string stateFilePath,
            IDeviceIdentityStateProtector protector)
        {
            if (string.IsNullOrWhiteSpace(stateFilePath))
                throw new ArgumentException("Device identity path is required.", nameof(stateFilePath));

            _stateFilePath = Path.GetFullPath(stateFilePath);
            _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        }

        public async Task<DeviceIdentityResult> GetOrCreateAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (!File.Exists(_stateFilePath))
                    return await CreateAndStoreAsync(DeviceIdentityStatus.Created, cancellationToken);

                DeviceIdentityResult existing = await TryReadAsync(cancellationToken);
                if (existing != null)
                    return existing;

                string quarantinePath = BuildQuarantinePath();
                try
                {
                    File.Move(_stateFilePath, quarantinePath);
                    Logger.Error(
                        $"Event=DeviceIdentityStateCorrupted Action=Quarantined " +
                        $"QuarantineFile={Path.GetFileName(quarantinePath)}",
                        ModuleName);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Logger.Error(
                        $"Event=DeviceIdentityStateCorrupted Action=QuarantineFailed ErrorType={ex.GetType().Name}",
                        ModuleName);
                    return DeviceIdentityResult.Unavailable("CorruptStateQuarantineFailed");
                }

                return await CreateAndStoreAsync(
                    DeviceIdentityStatus.RecreatedAfterCorruption,
                    cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<DeviceIdentityResult> TryReadAsync(CancellationToken cancellationToken)
        {
            try
            {
                byte[] protectedBytes = await File.ReadAllBytesAsync(_stateFilePath, cancellationToken);
                if (protectedBytes.Length == 0)
                    return null;

                byte[] stateBytes = _protector.Unprotect(protectedBytes);
                string json = new UTF8Encoding(false, true).GetString(stateBytes);
                DeviceIdentityState state = JsonConvert.DeserializeObject<DeviceIdentityState>(json);
                if (state == null || state.SchemaVersion != 1 ||
                    !Guid.TryParseExact(state.DeviceId, "D", out Guid deviceId) ||
                    deviceId == Guid.Empty)
                {
                    return null;
                }

                return DeviceIdentityResult.Available(
                    DeviceIdentityStatus.Existing,
                    deviceId.ToString("D"));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is CryptographicException ||
                ex is JsonException ||
                ex is DecoderFallbackException)
            {
                Logger.Error(
                    $"Event=DeviceIdentityStateReadFailed ErrorType={ex.GetType().Name} " +
                    "Action=QuarantineAndRecreate",
                    ModuleName);
                return null;
            }
        }

        private async Task<DeviceIdentityResult> CreateAndStoreAsync(
            DeviceIdentityStatus status,
            CancellationToken cancellationToken)
        {
            string deviceId = Guid.NewGuid().ToString("D");
            var state = new DeviceIdentityState
            {
                DeviceId = deviceId,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            string directory = Path.GetDirectoryName(_stateFilePath);
            string temporaryPath = null;
            try
            {
                Directory.CreateDirectory(directory);
                byte[] plaintext = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(state));
                byte[] protectedBytes = _protector.Protect(plaintext);
                temporaryPath = Path.Combine(directory, ".device-identity-" + Guid.NewGuid().ToString("N") + ".tmp");

                await WriteDurableFileAsync(temporaryPath, protectedBytes, cancellationToken);
                if (File.Exists(_stateFilePath))
                {
                    string backupPath = _stateFilePath + ".backup";
                    File.Replace(temporaryPath, _stateFilePath, backupPath, true);
                    temporaryPath = null;
                    TryDelete(backupPath);
                }
                else
                {
                    try
                    {
                        File.Move(temporaryPath, _stateFilePath);
                        temporaryPath = null;
                    }
                    catch (IOException) when (File.Exists(_stateFilePath))
                    {
                        TryDelete(temporaryPath);
                        temporaryPath = null;
                        DeviceIdentityResult concurrentResult = await TryReadAsync(cancellationToken);
                        if (concurrentResult != null)
                            return concurrentResult;

                        return DeviceIdentityResult.Unavailable("ConcurrentStateCreationFailed");
                    }
                }

                Logger.Info($"Event=DeviceIdentityReady Status={status}", ModuleName);
                return DeviceIdentityResult.Available(status, deviceId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is CryptographicException ||
                ex is JsonException)
            {
                Logger.Error(
                    $"Event=DeviceIdentityCreationFailed ErrorType={ex.GetType().Name}",
                    ModuleName);
                return DeviceIdentityResult.Unavailable("DeviceIdentityCreationFailed");
            }
            finally
            {
                if (temporaryPath != null)
                    TryDelete(temporaryPath);
            }
        }

        private string BuildQuarantinePath()
        {
            return _stateFilePath + ".corrupt-" +
                   DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" +
                   Guid.NewGuid().ToString("N");
        }

        private static async Task WriteDurableFileAsync(
            string path,
            byte[] bytes,
            CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(true);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
