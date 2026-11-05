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
    public sealed class FileApiConfigurationCache : IApiConfigurationCache
    {
        private readonly string _path;
        private readonly IApiSessionProtector _protector;
        private readonly string _legacyPath;

        public FileApiConfigurationCache(string path = null, IApiSessionProtector protector = null)
        {
            _path = path ?? Path.Combine(AppPaths.UserDataFolder, "configuration-current.dpapi");
            _protector = protector ?? new DpapiApiConfigurationProtector();
            _legacyPath = Path.Combine(AppPaths.ProgramDataFolder, "configuration-current.dpapi");
        }

        public async Task<ApiConfigurationResponse> LoadAsync(string deviceId, CancellationToken cancellationToken)
        {
            DeleteLegacyCache();
            if (string.IsNullOrWhiteSpace(deviceId) || !File.Exists(_path)) return null;
            try
            {
                byte[] protectedBytes = await File.ReadAllBytesAsync(_path, cancellationToken);
                byte[] plaintext = _protector.Unprotect(protectedBytes);
                ApiConfigurationResponse result = JsonConvert.DeserializeObject<ApiConfigurationResponse>(
                    new UTF8Encoding(false, true).GetString(plaintext));
                if (result?.Client is not null)
                {
                    // A cache written by an older release may still contain these
                    // fields. Service settings must never re-enter ordinary config.
                    result.Client.IdentificationCode = null;
                    result.Client.ChzToken = null;
                }
                return string.Equals(result?.Device?.DeviceId, deviceId, StringComparison.Ordinal) ? result : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is CryptographicException || ex is JsonException ||
                                       ex is DecoderFallbackException)
            {
                return null;
            }
        }

        public async Task SaveAsync(ApiConfigurationResponse configuration, CancellationToken cancellationToken)
        {
            if (configuration?.Client == null || string.IsNullOrWhiteSpace(configuration.Device?.DeviceId))
                throw new ArgumentException("Complete current-client configuration is required.", nameof(configuration));
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            byte[] plaintext = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(
                WithoutServiceSecrets(configuration)));
            byte[] protectedBytes = _protector.Protect(plaintext);
            string temporaryPath = _path + ".tmp";
            await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken);
            File.Move(temporaryPath, _path, true);
            DeleteLegacyCache();
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(_path)) File.Delete(_path);
            DeleteLegacyCache();
            return Task.CompletedTask;
        }

        private void DeleteLegacyCache()
        {
            if (string.Equals(_path, _legacyPath, StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                if (File.Exists(_legacyPath)) File.Delete(_legacyPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static ApiConfigurationResponse WithoutServiceSecrets(ApiConfigurationResponse configuration) => new()
        {
            ConfigurationRevision = configuration.ConfigurationRevision,
            Client = configuration.Client is null ? null : new ApiClientConfiguration
            {
                ClientId = configuration.Client.ClientId,
                Name = configuration.Client.Name,
                Inn = configuration.Client.Inn,
                Architecture = configuration.Client.Architecture,
                HasLmDatabaseBackup = configuration.Client.HasLmDatabaseBackup,
                RuDesktopEnabled = configuration.Client.RuDesktopEnabled,
                RuDesktopAutoOfferPasswordSetup = configuration.Client.RuDesktopAutoOfferPasswordSetup
            },
            Device = configuration.Device,
            Components = configuration.Components
        };
    }
}
