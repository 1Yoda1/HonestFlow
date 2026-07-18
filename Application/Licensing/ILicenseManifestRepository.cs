using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.Licensing
{
    public interface ILicenseManifestRepository
    {
        Task<LicenseManifestReadResult> ReadAsync(CancellationToken cancellationToken);
    }
}
