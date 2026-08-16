using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Models;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Configuration
{
    /// <summary>
    /// Stores only a historical, device-bound client label for the startup UI.
    /// This is deliberately separate from credentials, sessions and license caches.
    /// </summary>
    public sealed class LastAuthorizedClientHintStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HonestFlow.LastAuthorizedClientHint.v1");
        private readonly string _path;

        public LastAuthorizedClientHintStore(string path = null)
        {
            _path = path ?? Path.Combine(AppPaths.ProgramDataFolder, "last-authorized-client-hint.dpapi");
        }

        public async Task<LastAuthorizedClientHint> LoadForDeviceAsync(
            string currentDeviceId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(currentDeviceId) || !File.Exists(_path))
                return null;

            try
            {
                byte[] protectedBytes = await File.ReadAllBytesAsync(_path, cancellationToken);
                byte[] plaintext = ProtectedData.Unprotect(
                    protectedBytes,
                    Entropy,
                    DataProtectionScope.LocalMachine);
                LastAuthorizedClientHint hint = JsonConvert.DeserializeObject<LastAuthorizedClientHint>(
                    new UTF8Encoding(false, true).GetString(plaintext));

                return IsValid(hint) && string.Equals(hint.DeviceId, currentDeviceId, StringComparison.Ordinal)
                    ? hint
                    : null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is System.Security.SecurityException ||
                                       ex is CryptographicException || ex is JsonException ||
                                       ex is DecoderFallbackException)
            {
                Logger.Warning(
                    $"Event=LastAuthorizedClientHintReadFailed ErrorType={ex.GetType().Name}",
                    nameof(LastAuthorizedClientHintStore));
                return null;
            }
        }

        public async Task SaveAsync(LastAuthorizedClientHint hint, CancellationToken cancellationToken)
        {
            if (!IsValid(hint))
                throw new ArgumentException("Last authorized client hint is incomplete.", nameof(hint));

            string directory = Path.GetDirectoryName(_path);
            Directory.CreateDirectory(directory);
            hint.SavedAtUtc ??= DateTimeOffset.UtcNow;
            byte[] plaintext = new UTF8Encoding(false).GetBytes(JsonConvert.SerializeObject(hint));
            byte[] protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.LocalMachine);
            string temporaryPath = _path + ".tmp-" + Guid.NewGuid().ToString("N");

            try
            {
                await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken);
                File.Move(temporaryPath, _path, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private static bool IsValid(LastAuthorizedClientHint hint) =>
            hint != null &&
            !string.IsNullOrWhiteSpace(hint.DeviceId) &&
            !string.IsNullOrWhiteSpace(hint.ClientId) &&
            !string.IsNullOrWhiteSpace(hint.ClientName);
    }

    public sealed class LastAuthorizedClientHint
    {
        public string DeviceId { get; set; }
        public string ClientId { get; set; }
        public string ClientName { get; set; }
        public DateTimeOffset? SavedAtUtc { get; set; }

        public static LastAuthorizedClientHint CreateForAllowed(
            IPData client,
            LicenseObservationSnapshot snapshot,
            string currentDeviceId)
        {
            if (client == null || snapshot?.Decision != LicenseDecision.Allowed ||
                string.IsNullOrWhiteSpace(currentDeviceId) ||
                string.IsNullOrWhiteSpace(client.ClientId) || string.IsNullOrWhiteSpace(client.Name) ||
                string.IsNullOrWhiteSpace(snapshot.ClientId) || string.IsNullOrWhiteSpace(snapshot.DeviceId) ||
                !string.Equals(client.ClientId, snapshot.ClientId, StringComparison.Ordinal) ||
                !string.Equals(currentDeviceId, snapshot.DeviceId, StringComparison.Ordinal))
            {
                return null;
            }

            return new LastAuthorizedClientHint
            {
                DeviceId = currentDeviceId,
                ClientId = client.ClientId,
                ClientName = client.Name,
                SavedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }
}
