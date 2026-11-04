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
        private readonly string _legacyPath;
        private readonly string _legacyRegistrationPath;

        public FileApiSessionStore(string path = null, IApiSessionProtector protector = null,
            string legacyPath = null)
        {
            _path = path ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HonestFlow",
                "api-session.dpapi");
            _protector = protector ?? new DpapiApiSessionProtector();
            _legacyPath = path is null
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "HonestFlow", "api-session.dpapi")
                : legacyPath;
            _legacyRegistrationPath = path is null
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "HonestFlow", "registration-continuation.dpapi")
                : null;
        }

        public async Task<ApiSession> LoadAsync(CancellationToken cancellationToken)
        {
            try
            {
                DeleteLegacySession();
                if (!File.Exists(_path))
                    return null;
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
            DeleteLegacySession();
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(_path))
                File.Delete(_path);
            DeleteLegacySession();
            return Task.CompletedTask;
        }

        private void DeleteLegacySession()
        {
            DeleteLegacyFile(_legacyPath);
            DeleteLegacyFile(_legacyRegistrationPath);
        }

        private void DeleteLegacyFile(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) &&
                !string.Equals(Path.GetFullPath(path), Path.GetFullPath(_path),
                    StringComparison.OrdinalIgnoreCase) &&
                File.Exists(path))
                File.Delete(path);
        }
    }
}
