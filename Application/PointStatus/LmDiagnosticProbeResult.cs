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

    public enum LmInnComparisonState
    {
        Match,
        Mismatch,
        Missing,
        Unknown
    }

    public sealed class LmDiagnosticProbeResult
    {
        public LmDiagnosticProbeResult(
            NodeStatus status,
            bool healthAvailable,
            LmDiagnosticProbeState state,
            string runtimeStatus = null,
            string error = null,
            LmInnComparisonState innComparison = LmInnComparisonState.Unknown,
            string actualInnMasked = null,
            string expectedInnMasked = null,
            string innComparisonDetails = null)
        {
            Status = status;
            HealthAvailable = healthAvailable;
            State = state;
            RuntimeStatus = runtimeStatus;
            Error = error;
            InnComparison = innComparison;
            ActualInnMasked = actualInnMasked;
            ExpectedInnMasked = expectedInnMasked;
            InnComparisonDetails = innComparisonDetails;
        }

        public NodeStatus Status { get; }
        public bool HealthAvailable { get; }
        public LmDiagnosticProbeState State { get; }
        public string RuntimeStatus { get; }
        public string Error { get; }
        public LmInnComparisonState InnComparison { get; }
        public string ActualInnMasked { get; }
        public string ExpectedInnMasked { get; }
        public string InnComparisonDetails { get; }
    }
}
