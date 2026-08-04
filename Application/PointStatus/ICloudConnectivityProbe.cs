using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.PointStatus
{
    public interface ICloudConnectivityProbe
    {
        Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
    }
}
