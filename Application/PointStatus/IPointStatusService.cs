using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Core;
using HonestFlow.Models;

namespace HonestFlow.Application.PointStatus
{
    public interface IPointStatusService
    {
        Task<PointStatusResult> CheckAsync(CancellationToken cancellationToken);

        Task<PointStatusResult> CheckAsync(IPData currentClient, CancellationToken cancellationToken) =>
            CheckAsync(cancellationToken);

        Task<PointStatusResult> CheckLocalAsync(
            LocalRuntimeContext runtime,
            CancellationToken cancellationToken) =>
            CheckAsync(cancellationToken);

        Task<PointStatusResult> CheckForClientAsync(
            IPData currentClient,
            CancellationToken cancellationToken) =>
            CheckAsync(currentClient, cancellationToken);
    }
}
