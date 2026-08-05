using System;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.Licensing
{
    public interface ILicenseManifestCache
    {
        Task<LicenseCacheWriteResult> SaveAsync(
            LicenseGrantRequest request,
            LicenseManifestReadResult onlineResult,
            DateTimeOffset successfulOnlineCheckUtc,
            CancellationToken cancellationToken);

        Task<LicenseCacheReadResult> ReadAsync(
            LicenseGrantRequest request,
            CancellationToken cancellationToken);
    }
}
