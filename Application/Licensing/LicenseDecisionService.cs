using System;
using System.Collections.Generic;
using System.Linq;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseDecisionService : ILicenseDecisionService
    {
        private readonly LicenseDecisionPolicy _policy;
        private readonly Func<DateTimeOffset> _utcNowProvider;

        public LicenseDecisionService(LicenseDecisionPolicy policy = null)
            : this(policy, () => DateTimeOffset.UtcNow)
        {
        }

        public LicenseDecisionService(
            LicenseDecisionPolicy policy,
            Func<DateTimeOffset> utcNowProvider)
        {
            _policy = policy ?? new LicenseDecisionPolicy();
            _utcNowProvider = utcNowProvider ?? throw new ArgumentNullException(nameof(utcNowProvider));
        }

        public LicenseDecisionResult Decide(LicenseDecisionContext context)
        {
            DateTimeOffset nowUtc = _utcNowProvider().ToUniversalTime();
            if (!TryValidateContext(context, nowUtc, out string invalidStateCode))
            {
                return Denied(
                    LicenseDecision.InvalidLicenseState,
                    "Состояние лицензии некорректно.",
                    invalidStateCode,
                    null,
                    null,
                    null);
            }

            LicenseManifest manifest = context.Manifest;
            ClientLicense client = manifest.Clients.FirstOrDefault(candidate =>
                candidate != null &&
                string.Equals(candidate.ClientId, context.ClientId, StringComparison.Ordinal));

            if (client == null)
            {
                return Denied(
                    LicenseDecision.ClientNotFound,
                    "Лицензия для указанного клиента не найдена.",
                    "LICENSE_CLIENT_NOT_FOUND",
                    null,
                    null,
                    null);
            }

            Version minimumVersion = ParseVersion(client.MinHonestFlowVersion);
            if (minimumVersion == null)
            {
                return Denied(
                    LicenseDecision.InvalidLicenseState,
                    "В лицензии указана некорректная минимальная версия HonestFlow.",
                    "LICENSE_MIN_VERSION_INVALID",
                    client,
                    null,
                    null);
            }

            if (!client.Enabled)
            {
                return Denied(
                    LicenseDecision.ClientDisabled,
                    "Лицензия клиента отключена.",
                    "LICENSE_CLIENT_DISABLED",
                    client,
                    null,
                    minimumVersion);
            }

            LicensedDevice device = (client.Devices ?? new List<LicensedDevice>()).FirstOrDefault(candidate =>
                candidate != null &&
                string.Equals(candidate.DeviceId, context.DeviceId, StringComparison.Ordinal));

            if (device == null)
            {
                return Denied(
                    LicenseDecision.DeviceNotRegistered,
                    "Устройство не зарегистрировано в лицензии клиента.",
                    "LICENSE_DEVICE_NOT_REGISTERED",
                    client,
                    null,
                    minimumVersion);
            }

            if (!device.Enabled)
            {
                return Denied(
                    LicenseDecision.DeviceDisabled,
                    "Устройство отключено в лицензии клиента.",
                    "LICENSE_DEVICE_DISABLED",
                    client,
                    null,
                    minimumVersion);
            }

            if (context.CurrentHonestFlowVersion < minimumVersion)
            {
                return Denied(
                    LicenseDecision.VersionTooOld,
                    $"Требуется HonestFlow версии {minimumVersion} или новее.",
                    "LICENSE_VERSION_TOO_OLD",
                    client,
                    null,
                    minimumVersion);
            }

            if (nowUtc > manifest.ValidUntilUtc)
            {
                return Denied(
                    LicenseDecision.ManifestExpired,
                    "Срок действия лицензионного manifest истёк.",
                    "LICENSE_MANIFEST_EXPIRED",
                    client,
                    GetOfflineGraceEnd(context, client),
                    minimumVersion);
            }

            DateTimeOffset? graceEndUtc = null;
            if (context.ManifestSource == LicenseManifestSource.Cache)
            {
                graceEndUtc = context.LastSuccessfulOnlineCheckUtc.Value
                    .ToUniversalTime()
                    .AddHours(client.OfflineGraceHours);
                if (nowUtc > graceEndUtc.Value)
                {
                    return Denied(
                        LicenseDecision.OfflineGraceExpired,
                        "Истёк допустимый срок автономной работы лицензии.",
                        "LICENSE_OFFLINE_GRACE_EXPIRED",
                        client,
                        graceEndUtc,
                        minimumVersion);
                }
            }

            return new LicenseDecisionResult(
                LicenseDecision.Allowed,
                DistinctFeatures(client.Features),
                "Лицензия действительна.",
                "LICENSE_ALLOWED",
                graceEndUtc,
                minimumVersion);
        }

        private bool TryValidateContext(
            LicenseDecisionContext context,
            DateTimeOffset nowUtc,
            out string technicalCode)
        {
            technicalCode = "LICENSE_STATE_INVALID";
            if (context == null ||
                string.IsNullOrEmpty(context.ClientId) ||
                string.IsNullOrEmpty(context.DeviceId) ||
                context.CurrentHonestFlowVersion == null ||
                context.Manifest == null ||
                !Enum.IsDefined(typeof(LicenseManifestSource), context.ManifestSource))
            {
                return false;
            }

            if (context.Manifest.SchemaVersion <= 0 ||
                LicenseManifestValidator.Validate(context.Manifest).Count > 0)
            {
                technicalCode = "LICENSE_MANIFEST_INVALID";
                return false;
            }

            if (context.ManifestSource == LicenseManifestSource.Cache)
            {
                if (!context.LastSuccessfulOnlineCheckUtc.HasValue)
                {
                    technicalCode = "LICENSE_ONLINE_CHECK_TIME_MISSING";
                    return false;
                }

                if (context.LastSuccessfulOnlineCheckUtc.Value.ToUniversalTime() > nowUtc)
                {
                    technicalCode = "LICENSE_ONLINE_CHECK_TIME_IN_FUTURE";
                    return false;
                }
            }

            return true;
        }

        private LicenseDecisionResult Denied(
            LicenseDecision decision,
            string message,
            string technicalCode,
            ClientLicense client,
            DateTimeOffset? graceEndUtc,
            Version minimumVersion)
        {
            return new LicenseDecisionResult(
                decision,
                GetDeniedFeatures(client),
                message,
                technicalCode,
                graceEndUtc,
                minimumVersion);
        }

        private IReadOnlyCollection<LicenseFeature> GetDeniedFeatures(ClientLicense client)
        {
            if (client?.Features == null)
                return Array.Empty<LicenseFeature>();

            var allowed = new List<LicenseFeature>();
            if (_policy.AllowDiagnosticsWhenDenied && client.Features.Contains(LicenseFeature.Diagnostics))
                allowed.Add(LicenseFeature.Diagnostics);
            if (_policy.AllowSendLogsWhenDenied && client.Features.Contains(LicenseFeature.SendLogs))
                allowed.Add(LicenseFeature.SendLogs);
            return allowed;
        }

        private static IReadOnlyCollection<LicenseFeature> DistinctFeatures(
            List<LicenseFeature> features)
        {
            return features == null
                ? Array.Empty<LicenseFeature>()
                : features.Distinct().ToArray();
        }

        private static Version ParseVersion(string value)
        {
            return Version.TryParse(value, out Version version) ? version : null;
        }

        private static DateTimeOffset? GetOfflineGraceEnd(
            LicenseDecisionContext context,
            ClientLicense client)
        {
            if (context.ManifestSource != LicenseManifestSource.Cache ||
                !context.LastSuccessfulOnlineCheckUtc.HasValue)
            {
                return null;
            }

            return context.LastSuccessfulOnlineCheckUtc.Value
                .ToUniversalTime()
                .AddHours(client.OfflineGraceHours);
        }
    }
}
