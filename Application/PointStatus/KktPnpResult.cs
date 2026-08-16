using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.PointStatus
{
    public static class KktPnpDeviceMatcher
    {
        public static bool IsAtol(string name, string friendlyName, string manufacturer) =>
            ContainsAtol(name) || ContainsAtol(friendlyName) || ContainsAtol(manufacturer);

        private static bool ContainsAtol(string value) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.IndexOf("ATOL", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public enum KktPnpResultKind
    {
        Detected,
        NotDetected,
        Unavailable
    }

    public sealed class KktPnpResult
    {
        private KktPnpResult(KktPnpResultKind kind, string details)
        {
            Kind = kind;
            Details = details ?? string.Empty;
        }

        public KktPnpResultKind Kind { get; }
        public string Details { get; }

        public static KktPnpResult Detected(string details) => new(KktPnpResultKind.Detected, details);
        public static KktPnpResult NotDetected() => new(KktPnpResultKind.NotDetected, "ATOL PnP device not found.");
        public static KktPnpResult Unavailable(string details) => new(KktPnpResultKind.Unavailable, details);
    }

    public interface IKktPnpProbe
    {
        Task<KktPnpResult> DetectAsync(CancellationToken cancellationToken);
    }
}
