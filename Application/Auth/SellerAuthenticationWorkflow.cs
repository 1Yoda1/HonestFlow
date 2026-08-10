using System;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Models;

namespace HonestFlow.Application.Auth
{
    public sealed class SellerAuthenticationWorkflow
    {
        private readonly IAuthService _authService;
        private readonly ILicenseObservationSnapshotStore _snapshotStore;

        public SellerAuthenticationWorkflow(
            IAuthService authService,
            ILicenseObservationSnapshotStore snapshotStore)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
        }

        public bool ReportsLicenseProgress => _authService is ILicenseAuthenticatingAuthService;

        public async Task<LicenseAuthenticationResult> AuthenticateAsync(
            string password,
            IProgress<LicenseAuthenticationProgress> progress,
            CancellationToken cancellationToken)
        {
            if (_authService is ILicenseAuthenticatingAuthService licenseAuth)
                return await licenseAuth.AuthenticateAsync(password, progress, cancellationToken);

            IPData client = _authService.Authenticate(password);
            return new LicenseAuthenticationResult(client, _snapshotStore.Current);
        }

        public async Task<LicenseAuthenticationResult> AuthenticateAsync(
            string login,
            string password,
            IProgress<LicenseAuthenticationProgress> progress,
            CancellationToken cancellationToken)
        {
            if (_authService is IApiCredentialAuthService apiAuth)
                return await apiAuth.AuthenticateAsync(login, password, progress, cancellationToken);
            return await AuthenticateAsync(password, progress, cancellationToken);
        }
    }
}
