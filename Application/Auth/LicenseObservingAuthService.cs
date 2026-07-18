using System;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Models;
using HonestFlow.Infrastructure;

namespace HonestFlow.Application.Auth
{
    public sealed class LicenseObservingAuthService : ILicenseAuthenticatingAuthService
    {
        private readonly IAuthService _inner;
        private readonly ILicenseObservationService _observationService;

        public LicenseObservingAuthService(
            IAuthService inner,
            ILicenseObservationService observationService)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _observationService = observationService ?? throw new ArgumentNullException(nameof(observationService));
        }

        public void LoadIpList()
        {
            _inner.LoadIpList();
        }

        public IPData Authenticate(string password)
        {
            IPData client = _inner.Authenticate(password);
            if (client != null)
                _ = ObserveSafelyAsync(client);
            return client;
        }

        public async Task<LicenseAuthenticationResult> AuthenticateAsync(
            string password,
            IProgress<LicenseAuthenticationProgress> progress,
            CancellationToken cancellationToken)
        {
            progress?.Report(new LicenseAuthenticationProgress(
                LicenseAuthenticationStage.CheckingPassword));
            IPData client = _inner.Authenticate(password);
            if (client == null)
                return new LicenseAuthenticationResult(null, null);

            progress?.Report(new LicenseAuthenticationProgress(
                LicenseAuthenticationStage.ClientResolved,
                client.Name));
            progress?.Report(new LicenseAuthenticationProgress(
                LicenseAuthenticationStage.CheckingDeviceAndLicense,
                client.Name));
            LicenseObservationSnapshot snapshot = await _observationService.ObserveAsync(
                client,
                cancellationToken);
            progress?.Report(new LicenseAuthenticationProgress(
                LicenseAuthenticationStage.Completed,
                client.Name));
            return new LicenseAuthenticationResult(client, snapshot);
        }

        private async System.Threading.Tasks.Task ObserveSafelyAsync(IPData client)
        {
            try
            {
                await _observationService.ObserveAsync(client, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.Error(
                    $"Event=LicenseObservationBackgroundFailed ErrorType={ex.GetType().Name}",
                    nameof(LicenseObservingAuthService));
            }
        }
    }
}
