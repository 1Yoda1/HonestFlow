using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.PointStatus
{
    public interface IKktPortProbe
    {
        Task<KktPortProbeResult> CheckAsync(CancellationToken cancellationToken);
    }

    public sealed class KktPortProbeResult
    {
        private KktPortProbeResult(bool isAvailable, string errorCategory)
        {
            IsAvailable = isAvailable;
            ErrorCategory = errorCategory;
        }

        public bool IsAvailable { get; }
        public string ErrorCategory { get; }

        public static KktPortProbeResult Available() => new(true, null);
        public static KktPortProbeResult Unavailable(string errorCategory) => new(false, errorCategory);
    }
}
