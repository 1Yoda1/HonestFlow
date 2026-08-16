using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.PointStatus
{
    public static class DiagnosticsDebugMode
    {
        public const string CommandLineSwitch = "--diagnostics-debug";
        public const string EnvironmentVariable = "HONESTFLOW_DIAGNOSTICS_DEBUG";
        public static readonly bool DefaultEnabled = true;

        public static bool IsEnabled(string[] args, string environmentValue = null)
        {
            bool commandLine = args?.Any(arg =>
                string.Equals(arg, CommandLineSwitch, StringComparison.OrdinalIgnoreCase)) == true;
            string value = environmentValue ?? Environment.GetEnvironmentVariable(EnvironmentVariable);
            bool environment = string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            return DefaultEnabled || commandLine || environment;
        }
    }

    public sealed class DeveloperDiagnosticsSession
    {
        private readonly Func<CancellationToken, Task<DiagnosticsSnapshot>> _refresh;

        public DeveloperDiagnosticsSession(
            DiagnosticsSnapshot snapshot,
            Func<CancellationToken, Task<DiagnosticsSnapshot>> refresh)
        {
            Snapshot = snapshot;
            _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        }

        public DiagnosticsSnapshot Snapshot { get; private set; }

        public async Task<DiagnosticsSnapshot> RefreshAsync(CancellationToken cancellationToken)
        {
            Snapshot = await _refresh(cancellationToken).ConfigureAwait(false) ??
                throw new InvalidOperationException("Diagnostics refresh returned no snapshot.");
            return Snapshot;
        }
    }
}
