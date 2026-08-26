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
        private KktDriverProbeResult(string requiredArchitecture, bool x86DriverFound, bool x64DriverFound, string installedVersion, string errorCategory)
        {
            ExpectedArchitecture = NormalizeArchitecture(requiredArchitecture);
            X86DriverFound = x86DriverFound;
            X64DriverFound = x64DriverFound;
            DriverFound = ExpectedArchitecture == "x86" ? X86DriverFound : X64DriverFound;
            InstalledVersion = installedVersion;
            ErrorCategory = errorCategory;
        }

        public string ExpectedArchitecture { get; }
        public string RequiredArchitecture => ExpectedArchitecture;
        public bool X86DriverFound { get; }
        public bool X64DriverFound { get; }
        public bool DriverFound { get; }
        public bool HasArchitectureMismatch => IsAvailable && !DriverFound && (X86DriverFound || X64DriverFound);
        public string InstalledVersion { get; }
        public string ErrorCategory { get; }
        public bool IsAvailable => string.IsNullOrWhiteSpace(ErrorCategory);

        public static KktDriverProbeResult Found(string requiredArchitecture, string installedVersion)
        {
            string expected = NormalizeArchitecture(requiredArchitecture);
            return new(expected, expected == "x86", expected == "x64", installedVersion, null);
        }

        public static KktDriverProbeResult NotFound(string requiredArchitecture) =>
            new(requiredArchitecture, false, false, null, null);

        public static KktDriverProbeResult ArchitectureMismatch(string requiredArchitecture, string installedArchitecture, string installedVersion) =>
            new(requiredArchitecture, installedArchitecture == "x86", installedArchitecture == "x64", installedVersion, null);

        public static KktDriverProbeResult Unavailable(string requiredArchitecture, string errorCategory) =>
            new(requiredArchitecture, false, false, null, errorCategory);

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
