using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Configuration;

namespace HonestFlow.Infrastructure.Licensing
{
    public sealed class DpapiTrustedLicenseClock : ITrustedLicenseClock
    {
        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes("HonestFlow.LicenseClock.v1");
        private readonly string _path;
        private readonly object _sync = new();
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private DateTimeOffset _anchorUtc;

        public DpapiTrustedLicenseClock()
            : this(Path.Combine(AppPaths.ProgramDataFolder, "license-clock.dpapi"))
        {
        }

        public DpapiTrustedLicenseClock(string path)
        {
            _path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
            DateTimeOffset systemUtc = DateTimeOffset.UtcNow;
            DateTimeOffset? storedUtc = TryRead();
            _anchorUtc = storedUtc.HasValue && storedUtc.Value > systemUtc
                ? storedUtc.Value
                : systemUtc;
        }

        public DateTimeOffset UtcNow
        {
            get
            {
                lock (_sync)
                {
                    DateTimeOffset monotonicUtc = _anchorUtc.Add(_elapsed.Elapsed);
                    DateTimeOffset systemUtc = DateTimeOffset.UtcNow;
                    DateTimeOffset result = systemUtc > monotonicUtc ? systemUtc : monotonicUtc;
                    Persist(result);
                    return result;
                }
            }
        }

        public void ObserveTrustedTime(
            DateTimeOffset signedIssuedAtUtc,
            DateTimeOffset signedValidUntilUtc,
            DateTimeOffset? httpsDateUtc)
        {
            lock (_sync)
            {
                DateTimeOffset trustedUtc = signedIssuedAtUtc.ToUniversalTime();
                if (httpsDateUtc.HasValue)
                {
                    DateTimeOffset candidate = httpsDateUtc.Value.ToUniversalTime();
                    if (candidate >= trustedUtc && candidate <= signedValidUntilUtc.ToUniversalTime())
                        trustedUtc = candidate;
                }

                DateTimeOffset current = UtcNow;
                if (current > trustedUtc)
                    trustedUtc = current;

                _anchorUtc = trustedUtc;
                _elapsed.Restart();
                Persist(trustedUtc);
            }
        }

        private DateTimeOffset? TryRead()
        {
            try
            {
                if (!File.Exists(_path))
                    return null;
                byte[] plaintext = ProtectedData.Unprotect(
                    File.ReadAllBytes(_path), Entropy, DataProtectionScope.LocalMachine);
                string value = Encoding.UTF8.GetString(plaintext);
                return DateTimeOffset.TryParseExact(
                    value,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset result)
                    ? result
                    : null;
            }
            catch (Exception ex) when (
                ex is IOException || ex is UnauthorizedAccessException || ex is CryptographicException)
            {
                Logger.Warning(
                    $"Event=TrustedLicenseClockRead Status=Failed ErrorType={ex.GetType().Name}",
                    nameof(DpapiTrustedLicenseClock));
                return null;
            }
        }

        private void Persist(DateTimeOffset value)
        {
            try
            {
                string directory = Path.GetDirectoryName(_path);
                Directory.CreateDirectory(directory);
                byte[] plaintext = Encoding.UTF8.GetBytes(value.ToUniversalTime().ToString("O"));
                byte[] protectedBytes = ProtectedData.Protect(
                    plaintext, Entropy, DataProtectionScope.LocalMachine);
                string temporary = Path.Combine(
                    directory,
                    ".license-clock-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllBytes(temporary, protectedBytes);
                File.Move(temporary, _path, true);
            }
            catch (Exception ex) when (
                ex is IOException || ex is UnauthorizedAccessException || ex is CryptographicException)
            {
                Logger.Warning(
                    $"Event=TrustedLicenseClockWrite Status=Failed ErrorType={ex.GetType().Name}",
                    nameof(DpapiTrustedLicenseClock));
            }
        }
    }
}
