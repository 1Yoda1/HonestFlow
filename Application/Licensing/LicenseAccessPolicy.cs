using System;
using System.Collections.Generic;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseAccessPolicy : ILicenseAccessPolicy
    {
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

            if (IsBaseAccessFeature(feature))
                return Allowed("LICENSE_BASE_ACCESS");

            LicenseObservationSnapshot snapshot = _snapshotStore.Current;
            if (snapshot == null)
                return new LicenseAccessResult(
                    false,
                    "LICENSE_DECISION_PENDING",
                    "Заявка на лицензирование ещё не подтверждена.");

            IReadOnlyCollection<LicenseFeature> allowed = GetAllowedFeatures(snapshot);
            return IsGranted(allowed, feature)
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
                case LicenseDecision.ClientDisabled:
                case LicenseDecision.DeviceNotRegistered:
                case LicenseDecision.DeviceDisabled:
                case LicenseDecision.OfflineGraceExpired:
                case LicenseDecision.ClientNotFound:
                case LicenseDecision.ManifestExpired:
                case LicenseDecision.InvalidLicenseState:
                default:
                    return Array.Empty<LicenseFeature>();
            }
        }

        private static LicenseAccessResult Allowed(string technicalCode) =>
            new(true, technicalCode, string.Empty);

        private static bool IsGranted(
            IReadOnlyCollection<LicenseFeature> features,
            LicenseFeature requested)
        {
            if (Contains(features, requested))
                return true;

            return requested switch
            {
                LicenseFeature.ViewPointStatus =>
                    Contains(features, LicenseFeature.ViewAndRepair) ||
                    Contains(features, LicenseFeature.Diagnostics),
                LicenseFeature.ManageServices =>
                    Contains(features, LicenseFeature.ViewAndRepair) ||
                    Contains(features, LicenseFeature.AutoFix),
                LicenseFeature.RecoverLmServices =>
                    Contains(features, LicenseFeature.ViewAndRepair) ||
                    Contains(features, LicenseFeature.AutoFix),
                LicenseFeature.InitializeLm =>
                    Contains(features, LicenseFeature.ViewAndRepair) ||
                    Contains(features, LicenseFeature.AutoFix),
                LicenseFeature.OpenLocalTools =>
                    Contains(features, LicenseFeature.ViewAndRepair) ||
                    Contains(features, LicenseFeature.ManualTools),
                LicenseFeature.InstallComponents =>
                    Contains(features, LicenseFeature.InstallAndMaintenance) ||
                    Contains(features, LicenseFeature.Install),
                LicenseFeature.ReinstallComponents =>
                    Contains(features, LicenseFeature.InstallAndMaintenance) ||
                    Contains(features, LicenseFeature.Repair),
                LicenseFeature.RestoreLmDatabase =>
                    Contains(features, LicenseFeature.InstallAndMaintenance) ||
                    Contains(features, LicenseFeature.Repair),
                _ => false
            };
        }

        private static bool IsBaseAccessFeature(LicenseFeature feature) =>
            feature == LicenseFeature.Diagnostics ||
            feature == LicenseFeature.SendLogs ||
            feature == LicenseFeature.CollectDiagnostics ||
            feature == LicenseFeature.SendDiagnostics ||
            feature == LicenseFeature.RequestHelp ||
            feature == LicenseFeature.InstallRuDesktop ||
            feature == LicenseFeature.ConfigureRuDesktop;

        private static bool Contains(
            IReadOnlyCollection<LicenseFeature> features,
            LicenseFeature feature)
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
