using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.PointStatus
{
    public interface IEsmApiPortProbe
    {
        Task<EsmApiPortProbeResult> CheckAsync(CancellationToken cancellationToken);
    }

    public sealed class EsmApiPortProbeResult
    {
        private EsmApiPortProbeResult(bool isAvailable, int? port, string errorCategory)
        {
            IsAvailable = isAvailable;
            Port = port;
            ErrorCategory = errorCategory;
        }

        public bool IsAvailable { get; }
        public int? Port { get; }
        public string ErrorCategory { get; }

        public static EsmApiPortProbeResult Available(int port) => new(true, port, null);
        public static EsmApiPortProbeResult Unavailable(int? port, string errorCategory) => new(false, port, errorCategory);
    }
}
