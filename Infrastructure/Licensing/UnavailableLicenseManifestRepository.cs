using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;

namespace HonestFlow.Infrastructure.Licensing
{
    internal sealed class UnavailableLicenseManifestRepository : ILicenseManifestRepository
    {
        public Task<LicenseManifestReadResult> ReadAsync(
            LicenseGrantRequest request,
            CancellationToken cancellationToken)
        {
            _ = request ?? throw new System.ArgumentNullException(nameof(request));
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(LicenseManifestReadResult.Failure(
                LicenseManifestReadStatus.ServerError,
                "LicenseRemoteConfigurationMissing"));
        }
    }
}
