using System;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Models;

namespace HonestFlow.Application.Auth
{
    public interface ILicenseObservationRefresher
    {
        Task<LicenseObservationSnapshot> RefreshLicenseAsync(
            IPData client,
            IProgress<LicenseAuthenticationProgress> progress,
            CancellationToken cancellationToken);
    }
}
