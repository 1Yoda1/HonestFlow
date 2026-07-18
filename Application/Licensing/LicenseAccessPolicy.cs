using System;
using System.Collections.Generic;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseAccessPolicy : ILicenseAccessPolicy
    {
        private static readonly IReadOnlyCollection<LicenseFeature> DiagnosticAndLogs =
            new[] { LicenseFeature.Diagnostics, LicenseFeature.SendLogs };

        private static readonly IReadOnlyCollection<LicenseFeature> DiagnosticsOnly =
            new[] { LicenseFeature.Diagnostics };

        private readonly LicenseEnforcementMode _mode;
        private readonly ILicenseObservationSnapshotStore _snapshotStore;

        public LicenseAccessPolicy(
            LicenseEnforcementMode mode,
            ILicenseObservationSnapshotStore snapshotStore)
        {
            _mode = mode;
            _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
        }

        public LicenseAccessResult Check(LicenseFeature feature)
        {
            if (_mode != LicenseEnforcementMode.Enforced)
                return Allowed("LICENSE_ENFORCEMENT_NOT_ACTIVE");

            LicenseObservationSnapshot snapshot = _snapshotStore.Current;
            if (snapshot == null)
                return Denied("LICENSE_DECISION_PENDING", "Проверка лицензии ещё не завершена. Доступна диагностика.", feature, DiagnosticAndLogs);

            IReadOnlyCollection<LicenseFeature> allowed = GetAllowedFeatures(snapshot);
            return Contains(allowed, feature)
                ? Allowed(snapshot.TechnicalCode)
                : new LicenseAccessResult(false, snapshot.TechnicalCode, BuildDeniedMessage(snapshot));
        }

        private static IReadOnlyCollection<LicenseFeature> GetAllowedFeatures(LicenseObservationSnapshot snapshot)
        {
            switch (snapshot.Decision)
            {
                case LicenseDecision.Allowed:
                    return snapshot.Features ?? Array.Empty<LicenseFeature>();
                case LicenseDecision.VersionTooOld:
                    return DiagnosticsOnly;
                case LicenseDecision.ClientDisabled:
                case LicenseDecision.DeviceNotRegistered:
                case LicenseDecision.DeviceDisabled:
                case LicenseDecision.OfflineGraceExpired:
                case LicenseDecision.ClientNotFound:
                case LicenseDecision.ManifestExpired:
                case LicenseDecision.InvalidLicenseState:
                default:
                    return DiagnosticAndLogs;
            }
        }

        private static LicenseAccessResult Denied(
            string technicalCode,
            string message,
            LicenseFeature requested,
            IReadOnlyCollection<LicenseFeature> allowed)
        {
            return Contains(allowed, requested)
                ? Allowed(technicalCode)
                : new LicenseAccessResult(false, technicalCode, message);
        }

        private static LicenseAccessResult Allowed(string technicalCode) =>
            new(true, technicalCode, string.Empty);

        private static bool Contains(IReadOnlyCollection<LicenseFeature> features, LicenseFeature feature)
        {
            if (features == null)
                return false;

            foreach (LicenseFeature item in features)
            {
                if (item == feature)
                    return true;
            }

            return false;
        }

        private static string BuildDeniedMessage(LicenseObservationSnapshot snapshot)
        {
            return string.IsNullOrWhiteSpace(snapshot.Message)
                ? "Операция недоступна по текущему состоянию лицензии."
                : snapshot.Message;
        }
    }
}
