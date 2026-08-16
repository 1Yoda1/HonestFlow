using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Models;

namespace HonestFlow.Application.PointStatus
{
    public interface IPointStatusService
    {
        Task<PointStatusResult> CheckAsync(CancellationToken cancellationToken);

        Task<PointStatusResult> CheckAsync(IPData currentClient, CancellationToken cancellationToken) =>
            CheckAsync(cancellationToken);
    }
}
