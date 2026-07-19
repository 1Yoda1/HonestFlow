using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Configuration;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Licensing
{
    public sealed class DpapiDeviceRegistrationDeliveryStateStore : IDeviceRegistrationDeliveryStateStore
    {
        // v2 intentionally invalidates the old one-time marker because registration
        // requests now carry the point address and must be delivered once again.
        private const string DeliveryKeyVersion = "v2-point-address";
        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes("HonestFlow.DeviceRegistrationDelivery.v1");
        private readonly string _path;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public DpapiDeviceRegistrationDeliveryStateStore()
            : this(Path.Combine(AppPaths.ProgramDataFolder, "device-registration-delivery.dpapi"))
        {
        }

        public DpapiDeviceRegistrationDeliveryStateStore(string path)
        {
            _path = string.IsNullOrWhiteSpace(path)
                ? throw new ArgumentException("State path is required.", nameof(path))
                : Path.GetFullPath(path);
        }

        public async Task<bool> WasSentAsync(
            string clientId,
            string deviceId,
            CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                HashSet<string> entries = await ReadAsync(cancellationToken);
                return entries.Contains(BuildEntryKey(clientId, deviceId));
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task MarkSentAsync(
            string clientId,
            string deviceId,
            CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                HashSet<string> entries = await ReadAsync(cancellationToken);
                if (!entries.Add(BuildEntryKey(clientId, deviceId)))
                    return;

                string directory = Path.GetDirectoryName(_path);
                Directory.CreateDirectory(directory);
                byte[] json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(entries));
                byte[] protectedBytes = ProtectedData.Protect(
                    json,
                    Entropy,
                    DataProtectionScope.CurrentUser);
                string temporary = Path.Combine(
                    directory,
                    ".device-registration-" + Guid.NewGuid().ToString("N") + ".tmp");
                await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken);
                File.Move(temporary, _path, true);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<HashSet<string>> ReadAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!File.Exists(_path))
                    return new HashSet<string>(StringComparer.Ordinal);

                byte[] protectedBytes = await File.ReadAllBytesAsync(_path, cancellationToken);
                byte[] json = ProtectedData.Unprotect(
                    protectedBytes,
                    Entropy,
                    DataProtectionScope.CurrentUser);
                return JsonConvert.DeserializeObject<HashSet<string>>(Encoding.UTF8.GetString(json)) ??
                       new HashSet<string>(StringComparer.Ordinal);
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
                Logger.Warning(
                    $"Event=DeviceRegistrationDeliveryState Status=Invalid ErrorType={ex.GetType().Name}",
                    nameof(DpapiDeviceRegistrationDeliveryStateStore));
                return new HashSet<string>(StringComparer.Ordinal);
            }
        }

        private static string BuildEntryKey(string clientId, string deviceId)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(
                DeliveryKeyVersion + "\n" +
                (clientId ?? string.Empty) + "\n" +
                (deviceId ?? string.Empty));
            return Convert.ToBase64String(sha256.ComputeHash(bytes));
        }
    }
}
