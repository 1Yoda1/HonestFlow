using System;
using System.Collections.Generic;
using System.Net.Http;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.DeviceIdentity;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Infrastructure.Api;

namespace HonestFlow.Infrastructure.Licensing
{
    public static class LicenseObservationBootstrap
    {
        public static IAuthService WrapAuthService(IAuthService authService)
        {
            if (authService == null)
                throw new ArgumentNullException(nameof(authService));

            LicenseRuntimeConfiguration configuration = LicenseRuntimeConfiguration.FromEnvironment();
            Dictionary<string, string> keys = EmbeddedLicenseTrust.CreateKeyRegistry();
            if (!string.IsNullOrWhiteSpace(configuration.KeyId) &&
                !string.IsNullOrWhiteSpace(configuration.PublicKeySubjectPublicKeyInfoBase64))
            {
                keys[configuration.KeyId] = configuration.PublicKeySubjectPublicKeyInfoBase64;
            }

            var signatureVerifier = new EcdsaLicenseSignatureVerifier(new LicensePublicKeyRegistry(keys));
            IApiSessionProvider apiProvider = authService as IApiSessionProvider;
            ILicenseManifestRepository remoteRepository = apiProvider != null
                ? new ApiLicenseManifestRepository(apiProvider.ApiSessionService, signatureVerifier, configuration.RequestTimeout)
                : CreateRemoteRepository(configuration, signatureVerifier);
            var trustedClock = new DpapiTrustedLicenseClock();
            var observer = new LicenseObservationService(
                remoteRepository,
                new FileLicenseManifestCache(signatureVerifier, new DpapiLicenseCacheMetadataProtector()),
                new FileDeviceIdentityService(new DpapiDeviceIdentityStateProtector()),
                new LicenseDecisionService(
                    new LicenseDecisionPolicy
                    {
                        AllowDiagnosticsWhenDenied = true,
                        AllowSendLogsWhenDenied = true
                    },
                    () => trustedClock.UtcNow),
                LicenseObservationSnapshotStore.Instance,
                configuration.EnforcementMode,
                trustedClock: trustedClock,
                onlineClientPolicyEnabledProvider:
                    apiProvider?.ApiSessionService is IApiClientAccessStateProvider access
                        ? () => access.LicensePolicyEnabled
                        : null);

            Logger.Info(
                $"Event=LicenseObservationConfigured Mode={configuration.EnforcementMode} " +
                $"RemoteSource={(authService is IApiSessionProvider ? "HonestLicenseApi" : configuration.ManifestUrl != null && configuration.SignatureUrl != null ? "DirectUrls" : "YandexPointer")} " +
                $"PublicKeyConfigured={keys.Count > 0}",
                nameof(LicenseObservationBootstrap));
            return new LicenseObservingAuthService(authService, observer);
        }

        private static ILicenseManifestRepository CreateRemoteRepository(
            LicenseRuntimeConfiguration configuration,
            ILicenseSignatureVerifier signatureVerifier)
        {
            var httpClient = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            if (configuration.ManifestUrl == null || configuration.SignatureUrl == null)
            {
                return new YandexPointerLicenseManifestRepository(
                    httpClient,
                    ConfigManager.GetYandexPublicKey(),
                    configuration.RequestTimeout,
                    configuration.MaxManifestBytes,
                    signatureVerifier);
            }

            return new HttpLicenseManifestRepository(
                httpClient,
                new LicenseManifestRepositoryOptions
                {
                    ManifestUrl = configuration.ManifestUrl,
                    SignatureUrl = configuration.SignatureUrl,
                    RequestTimeout = configuration.RequestTimeout,
                    MaxResponseBytes = configuration.MaxManifestBytes,
                    SupportedSchemaVersion = 1
                },
                signatureVerifier);
        }
    }
}
