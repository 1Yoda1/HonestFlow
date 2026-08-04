using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HonestFlow.Infrastructure.Configuration;
using Newtonsoft.Json;

namespace HonestFlow.Application.RemoteAccess
{
    public sealed class UnlicensedHelpRequestStore
    {
        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes("HonestFlow.UnlicensedHelpRequest.v1");
        private readonly string _path;
        private readonly object _sync = new();

        public UnlicensedHelpRequestStore()
            : this(Path.Combine(AppPaths.ProgramDataFolder, "unlicensed-help-requests.dpapi"))
        {
        }

        public UnlicensedHelpRequestStore(string path)
        {
            _path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
        }

        public bool WasSent(string clientId, string deviceId)
        {
            lock (_sync)
                return Read().Contains(BuildKey(clientId, deviceId));
        }

        public void MarkSent(string clientId, string deviceId)
        {
            lock (_sync)
            {
                HashSet<string> entries = Read();
                if (!entries.Add(BuildKey(clientId, deviceId)))
                    return;

                string directory = Path.GetDirectoryName(_path);
                Directory.CreateDirectory(directory);
                byte[] plaintext = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(entries));
                byte[] protectedBytes = ProtectedData.Protect(
                    plaintext, Entropy, DataProtectionScope.LocalMachine);
                string temporary = Path.Combine(
                    directory,
                    ".unlicensed-help-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllBytes(temporary, protectedBytes);
                File.Move(temporary, _path, true);
            }
        }

        private HashSet<string> Read()
        {
            try
            {
                if (!File.Exists(_path))
                    return new HashSet<string>(StringComparer.Ordinal);
                byte[] plaintext = ProtectedData.Unprotect(
                    File.ReadAllBytes(_path), Entropy, DataProtectionScope.LocalMachine);
                return JsonConvert.DeserializeObject<HashSet<string>>(
                           Encoding.UTF8.GetString(plaintext)) ??
                       new HashSet<string>(StringComparer.Ordinal);
            }
            catch (Exception ex) when (
                ex is IOException || ex is UnauthorizedAccessException ||
                ex is CryptographicException || ex is JsonException)
            {
                throw new InvalidOperationException(
                    "Не удалось проверить состояние бесплатной заявки помощи.", ex);
            }
        }

        private static string BuildKey(string clientId, string deviceId)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] value = Encoding.UTF8.GetBytes(
                (clientId ?? string.Empty).Trim() + "\n" +
                (deviceId ?? string.Empty).Trim());
            return Convert.ToBase64String(sha256.ComputeHash(value));
        }
    }
}
