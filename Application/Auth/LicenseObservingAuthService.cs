using System;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Models;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Api;

namespace HonestFlow.Application.Auth
{
    public sealed class LicenseObservingAuthService :
        ILicenseAuthenticatingAuthService,
        IApiCredentialAuthService,
        IApiSessionProvider,
        IApiConfigurationProvider,
        ILicenseObservationRefresher
    {
        private readonly IAuthService _inner;
        private readonly ILicenseObservationService _observationService;

        public IApiSessionService ApiSessionService =>
            (_inner as IApiSessionProvider)?.ApiSessionService;
        public ApiConfigurationResponse CurrentConfiguration =>
            (_inner as IApiConfigurationProvider)?.CurrentConfiguration;

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
            LicenseObservationSnapshot snapshot = await ObserveWithRetryAsync(
                client,
                cancellationToken);
            progress?.Report(new LicenseAuthenticationProgress(
                LicenseAuthenticationStage.Completed,
                client.Name));
            return new LicenseAuthenticationResult(client, snapshot);
        }

        public async Task<LicenseAuthenticationResult> AuthenticateAsync(
            string login,
            string password,
            IProgress<LicenseAuthenticationProgress> progress,
            CancellationToken cancellationToken)
        {
            if (_inner is not IApiCredentialAuthService apiAuth)
                return await AuthenticateAsync(password, progress, cancellationToken);

            LicenseAuthenticationResult authentication = await apiAuth.AuthenticateAsync(
                login, password, progress, cancellationToken);
            if (authentication.Client == null)
                return authentication;
            if (authentication.LicenseSnapshot?.TechnicalCode == "CLIENT_ACCESS_DISABLED")
                return authentication;
            progress?.Report(new LicenseAuthenticationProgress(
                LicenseAuthenticationStage.CheckingDeviceAndLicense,
                authentication.Client.Name));
            LicenseObservationSnapshot snapshot = await ObserveWithRetryAsync(authentication.Client, cancellationToken);
            progress?.Report(new LicenseAuthenticationProgress(
                LicenseAuthenticationStage.Completed,
                authentication.Client.Name));
            return new LicenseAuthenticationResult(authentication.Client, snapshot);
        }

        public async Task<LicenseAuthenticationResult> TryResumeAsync(
            IProgress<LicenseAuthenticationProgress> progress,
            CancellationToken cancellationToken)
        {
            if (_inner is not IApiCredentialAuthService apiAuth)
                return new LicenseAuthenticationResult(null, null);
            LicenseAuthenticationResult authentication = await apiAuth.TryResumeAsync(progress, cancellationToken);
            if (authentication.Client == null) return authentication;
            if (authentication.LicenseSnapshot?.TechnicalCode == "CLIENT_ACCESS_DISABLED")
                return authentication;
            progress?.Report(new LicenseAuthenticationProgress(
                LicenseAuthenticationStage.CheckingDeviceAndLicense,
                authentication.Client.Name));
            LicenseObservationSnapshot snapshot = await ObserveWithRetryAsync(authentication.Client, cancellationToken);
            progress?.Report(new LicenseAuthenticationProgress(
                LicenseAuthenticationStage.Completed,
                authentication.Client.Name));
            return new LicenseAuthenticationResult(authentication.Client, snapshot);
        }

        public async Task<LicenseObservationSnapshot> RefreshLicenseAsync(
            IPData client,
            IProgress<LicenseAuthenticationProgress> progress,
            CancellationToken cancellationToken)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));

            progress?.Report(new LicenseAuthenticationProgress(
                LicenseAuthenticationStage.ClientResolved,
                client.Name));
            progress?.Report(new LicenseAuthenticationProgress(
                LicenseAuthenticationStage.CheckingDeviceAndLicense,
                client.Name));
            LicenseObservationSnapshot snapshot = await ObserveWithRetryAsync(
                client,
                cancellationToken);
            progress?.Report(new LicenseAuthenticationProgress(
                LicenseAuthenticationStage.Completed,
                client.Name));
            return snapshot;
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

        private async Task<LicenseObservationSnapshot> ObserveWithRetryAsync(
            IPData client,
            CancellationToken cancellationToken)
        {
            LicenseObservationSnapshot snapshot = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                snapshot = await _observationService.ObserveAsync(client, cancellationToken);
                if (!IsTransientFailure(snapshot) || attempt == 3)
                    return snapshot;

                int delayMs = attempt == 1
                    ? Random.Shared.Next(700, 1501)
                    : Random.Shared.Next(1800, 3501);
                Logger.Warning(
                    $"Event=LicenseObservationRetry Attempt={attempt + 1} DelayMs={delayMs} " +
                    $"RemoteStatus={snapshot.RemoteStatus}",
                    nameof(LicenseObservingAuthService));
                await Task.Delay(delayMs, cancellationToken);
            }

            return snapshot;
        }

        private static bool IsTransientFailure(LicenseObservationSnapshot snapshot) =>
            snapshot != null &&
            (snapshot.RemoteStatus == LicenseManifestReadStatus.Timeout ||
             snapshot.RemoteStatus == LicenseManifestReadStatus.NetworkUnavailable ||
             snapshot.RemoteStatus == LicenseManifestReadStatus.ServerError);
    }
}
