using System;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.Auth
{
    public interface IApiCredentialAuthService : IAuthService
    {
        Task<LicenseAuthenticationResult> AuthenticateAsync(
            string login,
            string password,
            IProgress<LicenseAuthenticationProgress> progress,
            CancellationToken cancellationToken);

        Task<LicenseAuthenticationResult> TryResumeAsync(
            IProgress<LicenseAuthenticationProgress> progress,
            CancellationToken cancellationToken);
    }
}
