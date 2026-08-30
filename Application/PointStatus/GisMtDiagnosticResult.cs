using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.PointStatus
{
    public enum GisMtDiagnosticState
    {
        Healthy,
        Warning,
        Error,
        Unknown
    }

    public enum GisMtErrorKind
    {
        None,
        Connectivity,
        Application,
        Registration,
        ControlledChannel,
        Configuration,
        NoAvailableCdn,
        Other
    }

    public enum GisMtEvidenceState
    {
        Unknown,
        Healthy,
        Error
    }

    public sealed class GisMtDiagnosticResult
    {
        public GisMtDiagnosticState State { get; init; }
        public string Summary { get; init; } = string.Empty;
        public string Details { get; init; } = string.Empty;
        public int ConfiguredCdnCount { get; init; }
        public IReadOnlyList<string> ConfiguredCdns { get; init; } = Array.Empty<string>();
        public int CachedCdnCount { get; init; }
        public int AvailableCdnCount { get; init; }
        public int BlockedCdnCount { get; init; }
        public bool? CacheIsFresh { get; init; }
        public DateTimeOffset? CacheLastCheckedUtc { get; init; }
        public double? LastLatencyMs { get; init; }
        public string LastActualCdn { get; init; }
        public string LastLogicalCdn { get; init; }
        public GisMtEvidenceState CdnTransportState { get; init; }
        public DateTimeOffset? LastCdnTransportSuccessUtc { get; init; }
        public DateTimeOffset? LastCdnTransportErrorUtc { get; init; }
        public string LastCdnTransportError { get; init; }
        public GisMtEvidenceState ApplicationExchangeState { get; init; }
        public DateTimeOffset? LastSuccessfulGisExchangeUtc { get; init; }
        public DateTimeOffset? LastApplicationErrorUtc { get; init; }
        public string LastApplicationError { get; init; }
        public GisMtErrorKind LastApplicationErrorKind { get; init; }
        public GisMtEvidenceState ControlledChannelState { get; init; }
        public DateTimeOffset? LastControlledChannelSuccessUtc { get; init; }
        public DateTimeOffset? LastControlledChannelErrorUtc { get; init; }
        public string LastControlledChannelError { get; init; }
        public DateTimeOffset? EvidenceTimestampUtc { get; init; }
        public bool PossibleCdnStateMismatch { get; init; }
        public string ControlledChannelDetails { get; init; }
        public int LogBytesRead { get; init; }
        public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
    }

    public interface IGisMtDiagnosticProbe
    {
        Task<GisMtDiagnosticResult> CheckAsync(
            EsmStatusResult localRestStatus,
            CancellationToken cancellationToken);
    }
}
