using System;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.PointStatus
{
    public interface IKktDriverProbe
    {
        Task<KktDriverProbeResult> CheckAsync(string requiredArchitecture, CancellationToken cancellationToken);
    }

    public sealed class KktDriverProbeResult
    {
        private KktDriverProbeResult(string requiredArchitecture, bool driverFound, string installedVersion, string errorCategory)
        {
            RequiredArchitecture = NormalizeArchitecture(requiredArchitecture);
            DriverFound = driverFound;
            InstalledVersion = installedVersion;
            ErrorCategory = errorCategory;
        }

        public string RequiredArchitecture { get; }
        public bool DriverFound { get; }
        public string InstalledVersion { get; }
        public string ErrorCategory { get; }
        public bool IsAvailable => string.IsNullOrWhiteSpace(ErrorCategory);

        public static KktDriverProbeResult Found(string requiredArchitecture, string installedVersion) =>
            new(requiredArchitecture, true, installedVersion, null);

        public static KktDriverProbeResult NotFound(string requiredArchitecture) =>
            new(requiredArchitecture, false, null, null);

        public static KktDriverProbeResult Unavailable(string requiredArchitecture, string errorCategory) =>
            new(requiredArchitecture, false, null, errorCategory);

        public bool IsAtLeast(string version) =>
            TryParseVersion(InstalledVersion, out Version installed) &&
            TryParseVersion(version, out Version required) &&
            installed >= required;

        public bool IsBelow(string version) =>
            TryParseVersion(InstalledVersion, out Version installed) &&
            TryParseVersion(version, out Version required) &&
            installed < required;

        public bool HasKnownVersion => TryParseVersion(InstalledVersion, out _);

        public string ToLegacyDisplay() => DriverFound
            ? $"{InstalledVersion ?? "версия не определена"} ({RequiredArchitecture})"
            : "не установлен";

        public static string NormalizeArchitecture(string architecture) =>
            string.Equals(architecture?.Trim(), "x86", StringComparison.OrdinalIgnoreCase) ? "x86" : "x64";

        private static bool TryParseVersion(string value, out Version version)
        {
            string token = value?.Trim().Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries)[0];
            return Version.TryParse(token, out version);
        }
    }
}
