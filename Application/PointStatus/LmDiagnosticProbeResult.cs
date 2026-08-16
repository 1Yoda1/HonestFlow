namespace HonestFlow.Application.PointStatus
{
    public enum LmDiagnosticProbeState
    {
        Available,
        NotInstalled,
        ApiUnavailable,
        SyncError,
        NotConfigured,
        Initializing,
        Failure,
        Unknown
    }

    public sealed class LmDiagnosticProbeResult
    {
        public LmDiagnosticProbeResult(
            NodeStatus status,
            bool healthAvailable,
            LmDiagnosticProbeState state,
            string runtimeStatus = null,
            string error = null)
        {
            Status = status;
            HealthAvailable = healthAvailable;
            State = state;
            RuntimeStatus = runtimeStatus;
            Error = error;
        }

        public NodeStatus Status { get; }
        public bool HealthAvailable { get; }
        public LmDiagnosticProbeState State { get; }
        public string RuntimeStatus { get; }
        public string Error { get; }
    }
}
