using System;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.Licensing
{
    public interface ILicenseManifestCache
    {
        Task<LicenseCacheWriteResult> SaveAsync(
            LicenseManifestReadResult onlineResult,
            DateTimeOffset successfulOnlineCheckUtc,
            CancellationToken cancellationToken);

        Task<LicenseCacheReadResult> ReadAsync(CancellationToken cancellationToken);
    }
}
