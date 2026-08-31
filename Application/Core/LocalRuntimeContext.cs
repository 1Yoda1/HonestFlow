using System;

namespace HonestFlow.Application.Core
{
    /// <summary>
    /// Minimal local-only runtime data required by diagnostics probes.
    /// It deliberately carries no client, license, configuration, or session data.
    /// </summary>
    public sealed class LocalRuntimeContext
    {
        public LocalRuntimeContext(string operatingSystemArchitecture)
        {
            OperatingSystemArchitecture = NormalizeArchitecture(operatingSystemArchitecture);
        }

        public string OperatingSystemArchitecture { get; }

        public static LocalRuntimeContext CreateCurrent() =>
            new(Environment.Is64BitOperatingSystem ? "x64" : "x86");

        private static string NormalizeArchitecture(string architecture) =>
            string.Equals(architecture?.Trim(), "x86", StringComparison.OrdinalIgnoreCase)
                ? "x86"
                : "x64";
    }
}
