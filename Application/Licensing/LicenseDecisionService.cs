using System;
using System.Collections.Generic;
using System.Linq;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseDecisionService : ILicenseDecisionService
    {
        private readonly Func<DateTimeOffset> _utcNowProvider;

        public LicenseDecisionService(LicenseDecisionPolicy policy = null)
            : this(policy, () => DateTimeOffset.UtcNow) { }

        public LicenseDecisionService(
            LicenseDecisionPolicy policy,
            Func<DateTimeOffset> utcNowProvider)
        {
            _ = policy ?? new LicenseDecisionPolicy();
            _utcNowProvider = utcNowProvider ?? throw new ArgumentNullException(nameof(utcNowProvider));
        }

        public LicenseDecisionResult Decide(LicenseDecisionContext context)
        {
            DateTimeOffset nowUtc = _utcNowProvider().ToUniversalTime();
            if (!TryValidateContext(context, nowUtc, out string invalidStateCode))
                return Denied(LicenseDecision.InvalidLicenseState, "Состояние лицензии некорректно.", invalidStateCode);

            LicenseGrant grant = context.Grant;
            if (!string.Equals(grant.ClientId, context.ClientId, StringComparison.Ordinal))
                return Denied(LicenseDecision.ClientNotFound, "Лицензия выдана другому клиенту.", "LICENSE_GRANT_CLIENT_MISMATCH");

            if (!string.Equals(grant.DeviceId, context.DeviceId, StringComparison.Ordinal))
                return Denied(LicenseDecision.DeviceNotRegistered, "Лицензия выдана другому устройству.", "LICENSE_GRANT_DEVICE_MISMATCH");

            bool clientEnabled = context.ManifestSource == LicenseManifestSource.Remote &&
                                 context.OnlineClientPolicyEnabled.HasValue
                ? context.OnlineClientPolicyEnabled.Value
                : grant.ClientEnabled;
            if (!clientEnabled)
                return Denied(LicenseDecision.ClientDisabled, "Лицензия клиента отключена.", "LICENSE_CLIENT_DISABLED");

            if (!grant.DeviceEnabled)
                return Denied(LicenseDecision.DeviceDisabled, "Устройство отключено в лицензии.", "LICENSE_DEVICE_DISABLED");

            Version minimumVersion = ParseVersion(grant.MinHonestFlowVersion);
            if (minimumVersion == null)
                return Denied(LicenseDecision.InvalidLicenseState, "В лицензии указана некорректная минимальная версия HonestFlow.", "LICENSE_MIN_VERSION_INVALID");

            if (context.CurrentHonestFlowVersion < minimumVersion)
                return Denied(LicenseDecision.VersionTooOld, $"Требуется HonestFlow версии {minimumVersion} или новее.", "LICENSE_VERSION_TOO_OLD", null, minimumVersion, grant.PointAddress);

            if (nowUtc > grant.ValidUntilUtc)
                return Denied(LicenseDecision.ManifestExpired, "Срок действия подписанной лицензии истёк.", "LICENSE_GRANT_EXPIRED", GetOfflineGraceEnd(context, grant), minimumVersion, grant.PointAddress);

            DateTimeOffset? graceEndUtc = null;
            if (context.ManifestSource == LicenseManifestSource.Cache)
            {
                graceEndUtc = context.LastSuccessfulOnlineCheckUtc.Value
                    .ToUniversalTime()
                    .AddHours(grant.OfflineGraceHours);
                if (nowUtc > graceEndUtc.Value)
                    return Denied(LicenseDecision.OfflineGraceExpired, "Истёк допустимый срок автономной работы лицензии.", "LICENSE_OFFLINE_GRACE_EXPIRED", graceEndUtc, minimumVersion, grant.PointAddress);
            }

            IReadOnlyCollection<LicenseFeature> features = grant.OperatorDevice
                ? AllFeatures()
                : DistinctFeatures(grant.Features);
            return new LicenseDecisionResult(
                LicenseDecision.Allowed,
                features,
                grant.OperatorDevice ? "Операторское устройство имеет полный доступ." : "Лицензия действительна.",
                grant.OperatorDevice ? "LICENSE_OPERATOR_DEVICE_ALLOWED" : "LICENSE_ALLOWED",
                graceEndUtc,
                minimumVersion,
                grant.PointAddress);
        }

        private static bool TryValidateContext(
            LicenseDecisionContext context,
            DateTimeOffset nowUtc,
            out string technicalCode)
        {
            technicalCode = "LICENSE_STATE_INVALID";
            if (context == null ||
                string.IsNullOrEmpty(context.ClientId) ||
                string.IsNullOrEmpty(context.DeviceId) ||
                context.CurrentHonestFlowVersion == null ||
                context.Grant == null ||
                !Enum.IsDefined(typeof(LicenseManifestSource), context.ManifestSource))
                return false;

            if (LicenseGrantValidator.Validate(context.Grant).Count > 0)
            {
                technicalCode = "LICENSE_GRANT_INVALID";
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

        private static LicenseDecisionResult Denied(
            LicenseDecision decision,
            string message,
            string technicalCode,
            DateTimeOffset? graceEndUtc = null,
            Version minimumVersion = null,
            string pointAddress = null) =>
            new(decision, Array.Empty<LicenseFeature>(), message, technicalCode, graceEndUtc, minimumVersion, pointAddress);

        private static IReadOnlyCollection<LicenseFeature> DistinctFeatures(List<LicenseFeature> features) =>
            features == null ? Array.Empty<LicenseFeature>() : features.Distinct().ToArray();

        private static IReadOnlyCollection<LicenseFeature> AllFeatures() =>
            ((LicenseFeature[])Enum.GetValues(typeof(LicenseFeature))).ToArray();

        private static Version ParseVersion(string value) =>
            Version.TryParse(value, out Version version) ? version : null;

        private static DateTimeOffset? GetOfflineGraceEnd(
            LicenseDecisionContext context,
            LicenseGrant grant) =>
            context.ManifestSource == LicenseManifestSource.Cache && context.LastSuccessfulOnlineCheckUtc.HasValue
                ? context.LastSuccessfulOnlineCheckUtc.Value.ToUniversalTime().AddHours(grant.OfflineGraceHours)
                : null;
    }
}
