using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Configuration;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Api
{
    /// <summary>
    /// Keeps Service-only integration settings in the current Windows profile.
    /// This cache is used only after a locally validated Service entitlement.
    /// </summary>
    public sealed class FileApiServiceConfigurationCache : IApiServiceConfigurationCache
    {
        private readonly string _path;
        private readonly IApiSessionProtector _protector;

        public FileApiServiceConfigurationCache(string path = null, IApiSessionProtector protector = null)
        {
            _path = path ?? Path.Combine(AppPaths.UserDataFolder, "service-configuration.dpapi");
            _protector = protector ?? new DpapiApiSessionProtector();
        }

        public async Task<ApiServiceConfigurationResponse> LoadAsync(
            string clientId,
            string deviceId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(deviceId) || !File.Exists(_path))
                return null;

            try
            {
                byte[] protectedBytes = await File.ReadAllBytesAsync(_path, cancellationToken);
                byte[] plaintext = _protector.Unprotect(protectedBytes);
                ApiServiceConfigurationResponse result = JsonConvert.DeserializeObject<ApiServiceConfigurationResponse>(
                    new UTF8Encoding(false, true).GetString(plaintext));
                return string.Equals(result?.ClientId, clientId, StringComparison.Ordinal) &&
                       string.Equals(result?.DeviceId, deviceId, StringComparison.Ordinal)
                    ? result
                    : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is CryptographicException || ex is JsonException ||
                                       ex is DecoderFallbackException)
            {
                return null;
            }
        }

        public async Task SaveAsync(ApiServiceConfigurationResponse configuration, CancellationToken cancellationToken)
        {
            if (configuration is null || string.IsNullOrWhiteSpace(configuration.ClientId) ||
                string.IsNullOrWhiteSpace(configuration.DeviceId))
            {
                throw new ArgumentException("Complete Service configuration is required.", nameof(configuration));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            byte[] plaintext = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(configuration));
            byte[] protectedBytes = _protector.Protect(plaintext);
            string temporaryPath = _path + ".tmp";
            await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken);
            File.Move(temporaryPath, _path, true);
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(_path)) File.Delete(_path);
            return Task.CompletedTask;
        }
    }
}
