using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HonestFlow.Models;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Configuration
{
    public sealed class AuthorizedClientCache
    {
        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes("HonestFlow.AuthorizedClient.v1");
        private readonly string _path;

        public AuthorizedClientCache() : this(AppPaths.AuthorizedClientCacheFile) { }

        public AuthorizedClientCache(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public void Save(IPData client)
        {
            if (client == null || string.IsNullOrWhiteSpace(client.ClientId) ||
                string.IsNullOrEmpty(client.Password))
                throw new ArgumentException("Authorized client is incomplete.", nameof(client));

            byte[] plaintext = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(client));
            byte[] protectedData = ProtectedData.Protect(
                plaintext, Entropy, DataProtectionScope.LocalMachine);
            string directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(temporary, protectedData);
                if (File.Exists(_path))
                    File.Replace(temporary, _path, null, true);
                else
                    File.Move(temporary, _path);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        public IPData Load()
        {
            try
            {
                if (!File.Exists(_path))
                    return null;
                byte[] plaintext = ProtectedData.Unprotect(
                    File.ReadAllBytes(_path), Entropy, DataProtectionScope.LocalMachine);
                IPData client = JsonConvert.DeserializeObject<IPData>(
                    new UTF8Encoding(false, true).GetString(plaintext));
                return client != null && !string.IsNullOrWhiteSpace(client.ClientId) &&
                       !string.IsNullOrEmpty(client.Password) ? client : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is CryptographicException || ex is JsonException ||
                                       ex is DecoderFallbackException)
            {
                Logger.Warning($"Event=AuthorizedClientCacheReadFailed ErrorType={ex.GetType().Name}",
                    nameof(AuthorizedClientCache));
                return null;
            }
        }
    }
}
