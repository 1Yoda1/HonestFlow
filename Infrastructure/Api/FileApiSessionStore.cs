using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Security.Cryptography;

namespace HonestFlow.Infrastructure.Api
{
    public sealed class FileApiSessionStore : IApiSessionStore
    {
        private readonly string _path;
        private readonly IApiSessionProtector _protector;

        public FileApiSessionStore(string path = null, IApiSessionProtector protector = null)
        {
            _path = path ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "HonestFlow",
                "api-session.dpapi");
            _protector = protector ?? new DpapiApiSessionProtector();
        }

        public async Task<ApiSession> LoadAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(_path))
                return null;
            try
            {
                byte[] protectedBytes = await File.ReadAllBytesAsync(_path, cancellationToken);
                byte[] plaintext = _protector.Unprotect(protectedBytes);
                return JsonConvert.DeserializeObject<ApiSession>(new UTF8Encoding(false, true).GetString(plaintext));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is CryptographicException || ex is JsonException ||
                                       ex is DecoderFallbackException)
            {
                return null;
            }
        }

        public async Task SaveAsync(ApiSession session, CancellationToken cancellationToken)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            string directory = Path.GetDirectoryName(_path);
            Directory.CreateDirectory(directory);
            byte[] plaintext = new UTF8Encoding(false).GetBytes(JsonConvert.SerializeObject(session));
            byte[] protectedBytes = _protector.Protect(plaintext);
            string temporaryPath = _path + ".tmp";
            await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken);
            File.Move(temporaryPath, _path, true);
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(_path))
                File.Delete(_path);
            return Task.CompletedTask;
        }
    }
}
