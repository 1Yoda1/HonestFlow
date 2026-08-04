using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.PointStatus
{
    public interface IWindowsServiceSnapshotProvider
    {
        Task<ServiceSnapshot[]> GetSnapshotsAsync(CancellationToken cancellationToken);
    }
}
