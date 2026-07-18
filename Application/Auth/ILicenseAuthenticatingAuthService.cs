using System;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.Auth
{
    public interface ILicenseAuthenticatingAuthService : IAuthService
    {
        Task<LicenseAuthenticationResult> AuthenticateAsync(
            string password,
            IProgress<LicenseAuthenticationProgress> progress,
            CancellationToken cancellationToken);
    }
}
