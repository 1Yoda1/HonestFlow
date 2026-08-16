using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Core;
using HonestFlow.Application.DeviceIdentity;
using HonestFlow.Models;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Api
{
    public sealed class ApiAuthService : IApiCredentialAuthService, IApiSessionProvider
    {
        private readonly IApiSessionService _session;
        private readonly IDeviceIdentityService _deviceIdentity;
        private readonly ILogService _log;
        private readonly IApiConfigurationCache _configurationCache;

        public ApiAuthService(IApiSessionService session, IDeviceIdentityService deviceIdentity, ILogService log, IApiConfigurationCache configurationCache = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _deviceIdentity = deviceIdentity ?? throw new ArgumentNullException(nameof(deviceIdentity));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _configurationCache = configurationCache ?? new FileApiConfigurationCache();
        }

        public IApiSessionService ApiSessionService => _session;
        public void LoadIpList() { }
        public IPData Authenticate(string password) => null;

        public async Task<LicenseAuthenticationResult> AuthenticateAsync(
            string login, string password, IProgress<LicenseAuthenticationProgress> progress, CancellationToken cancellationToken)
        {
            progress?.Report(new LicenseAuthenticationProgress(LicenseAuthenticationStage.CheckingPassword));
            DeviceIdentityResult identity = await _deviceIdentity.GetOrCreateAsync(cancellationToken);
            if (!identity.IsAvailable) throw new InvalidOperationException("Device identity is unavailable.");
            ApiTokenResponse tokens = await _session.LoginAsync(
                login, password, identity.DeviceId, Environment.MachineName, cancellationToken);
            if (tokens.LicensePolicyEnabled == false)
                return ClientAccessDisabled(tokens.ClientId, tokens.ClientName, identity.DeviceId);
            if (tokens.DeviceRegistrationRequired)
            {
                return new LicenseAuthenticationResult(null, new HonestFlow.Application.Licensing.LicenseObservationSnapshot
                {
                    ObservedAtUtc = DateTimeOffset.UtcNow,
                    ClientId = tokens.ClientId,
                    ClientName = tokens.ClientName,
                    DeviceId = identity.DeviceId,
                    Decision = HonestFlow.Application.Licensing.LicenseDecision.DeviceNotRegistered,
                    TechnicalCode = "DEVICE_REGISTRATION_REQUIRED",
                    Message = "Устройство требует регистрации."
                });
            }

            ApiConfigurationResponse configuration = await LoadOnlineConfigurationAsync(cancellationToken);
            await _configurationCache.SaveAsync(configuration, cancellationToken);
            IPData client = Map(configuration);
            _log.LogDebug($"API authentication completed for client {client.ClientId}.");
            progress?.Report(new LicenseAuthenticationProgress(LicenseAuthenticationStage.ClientResolved, client.Name));
            return new LicenseAuthenticationResult(client, null);
        }

        public async Task<LicenseAuthenticationResult> TryResumeAsync(
            IProgress<LicenseAuthenticationProgress> progress,
            CancellationToken cancellationToken)
        {
            DeviceIdentityResult identity = await _deviceIdentity.GetOrCreateAsync(cancellationToken);
            if (!identity.IsAvailable) return new LicenseAuthenticationResult(null, null);
            progress?.Report(new LicenseAuthenticationProgress(LicenseAuthenticationStage.CheckingPassword));
            ApiConfigurationResponse configuration;
            try
            {
                if (_session is IApiSessionRefresher refresher &&
                    !await refresher.RefreshSessionAsync(cancellationToken))
                    return new LicenseAuthenticationResult(null, null);
                if (_session is IApiClientAccessStateProvider access && access.LicensePolicyEnabled == false)
                    return ClientAccessDisabled(access, identity.DeviceId);
                configuration = await LoadOnlineConfigurationAsync(cancellationToken);
                await _configurationCache.SaveAsync(configuration, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
                                       (ex is HttpRequestException ||
                                        ex is OperationCanceledException ||
                                        ex is ApiRequestException apiError &&
                                        (int)apiError.StatusCode >= 500))
            {
                configuration = await _configurationCache.LoadAsync(identity.DeviceId, cancellationToken);
                if (configuration == null) return new LicenseAuthenticationResult(null, null);
                _log.LogDebug("API unavailable; using protected current-client configuration cache.");
            }
            catch (ApiRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                                                 ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                await _configurationCache.ClearAsync(cancellationToken);
                return new LicenseAuthenticationResult(null, null);
            }

            IPData client = Map(configuration);
            progress?.Report(new LicenseAuthenticationProgress(LicenseAuthenticationStage.ClientResolved, client.Name));
            return new LicenseAuthenticationResult(client, null);
        }

        private static LicenseAuthenticationResult ClientAccessDisabled(
            string clientId, string clientName, string deviceId) =>
            new(new IPData { ClientId = clientId, Name = clientName }, new HonestFlow.Application.Licensing.LicenseObservationSnapshot
            {
                ObservedAtUtc = DateTimeOffset.UtcNow, ClientId = clientId, ClientName = clientName,
                DeviceId = deviceId, Decision = HonestFlow.Application.Licensing.LicenseDecision.ClientDisabled,
                TechnicalCode = "CLIENT_ACCESS_DISABLED", Message = "Доступ к HonestFlow для этого клиента отключён."
            });

        private static LicenseAuthenticationResult ClientAccessDisabled(
            IApiClientAccessStateProvider access, string deviceId) =>
            ClientAccessDisabled(access.ClientId, access.ClientName, deviceId);

        private async Task<ApiConfigurationResponse> LoadOnlineConfigurationAsync(CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/configuration/current");
            using HttpResponseMessage response = await _session.SendAuthorizedAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new ApiRequestException(response.StatusCode);
            ApiConfigurationResponse configuration = JsonConvert.DeserializeObject<ApiConfigurationResponse>(
                await response.Content.ReadAsStringAsync(cancellationToken));
            if (configuration?.Client == null || string.IsNullOrWhiteSpace(configuration.Device?.DeviceId))
                throw new InvalidOperationException("API returned an incomplete current-client configuration.");
            return configuration;
        }

        private static IPData Map(ApiConfigurationResponse configuration)
        {
            if (configuration?.Client == null || string.IsNullOrWhiteSpace(configuration.Client.ClientId))
                throw new InvalidOperationException("API returned an incomplete client configuration.");
            var versions = new VersionsData();
            foreach (ApiComponentConfiguration component in configuration.Components ?? new List<ApiComponentConfiguration>())
            {
                string name = component?.Component?.Replace("-", "").Replace("_", "");
                if (string.Equals(name, "LmModule", StringComparison.OrdinalIgnoreCase)) versions.LmModule = component.EffectiveVersion;
                else if (string.Equals(name, "AtolDriver", StringComparison.OrdinalIgnoreCase)) versions.AtolDriver = component.EffectiveVersion;
                else if (string.Equals(name, "ESM", StringComparison.OrdinalIgnoreCase)) versions.ESM = component.EffectiveVersion;
                else if (string.Equals(name, "Controller", StringComparison.OrdinalIgnoreCase)) versions.Controller = component.EffectiveVersion;
                else if (string.Equals(name, "HonestFlow", StringComparison.OrdinalIgnoreCase)) versions.HonestFlow = component.EffectiveVersion;
            }
            return new IPData
            {
                ClientId = configuration.Client.ClientId,
                Name = configuration.Client.Name,
                Architecture = configuration.Client.Architecture,
                HasLmDatabaseBackup = configuration.Client.HasLmDatabaseBackup,
                RuDesktop = new RuDesktopOptions
                {
                    Enabled = configuration.Client.RuDesktopEnabled,
                    AutoOfferPasswordSetup = configuration.Client.RuDesktopAutoOfferPasswordSetup
                },
                Versions = versions
            };
        }
    }
}
