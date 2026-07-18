using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Models;

namespace HonestFlow.Application.Licensing
{
    public interface ILicenseObservationService
    {
        Task<LicenseObservationSnapshot> ObserveAsync(
            IPData client,
            CancellationToken cancellationToken);
    }
}
