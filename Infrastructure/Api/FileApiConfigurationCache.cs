using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Api
{
    public sealed class FileApiConfigurationCache : IApiConfigurationCache
    {
        private readonly string _path;
        private readonly IApiSessionProtector _protector;

        public FileApiConfigurationCache(string path = null, IApiSessionProtector protector = null)
        {
            _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "HonestFlow", "configuration-current.dpapi");
            _protector = protector ?? new DpapiApiConfigurationProtector();
        }

        public async Task<ApiConfigurationResponse> LoadAsync(string deviceId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || !File.Exists(_path)) return null;
            try
            {
                byte[] protectedBytes = await File.ReadAllBytesAsync(_path, cancellationToken);
                byte[] plaintext = _protector.Unprotect(protectedBytes);
                ApiConfigurationResponse result = JsonConvert.DeserializeObject<ApiConfigurationResponse>(
                    new UTF8Encoding(false, true).GetString(plaintext));
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
