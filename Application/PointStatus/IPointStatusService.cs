using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.PointStatus
{
    public interface IPointStatusService
    {
        Task<PointStatusResult> CheckAsync(CancellationToken cancellationToken);
    }
}
