using System;
using System.Collections.Generic;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseAccessPolicy : ILicenseAccessPolicy
    {
        private readonly LicenseEnforcementMode _mode;
        private readonly ILicenseObservationSnapshotStore _snapshotStore;
        private readonly Func<string> _currentClientIdProvider;

        public LicenseAccessPolicy(
            LicenseEnforcementMode mode,
            ILicenseObservationSnapshotStore snapshotStore,
            Func<string> currentClientIdProvider = null)
        {
            _mode = mode;
            _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
            _currentClientIdProvider = currentClientIdProvider;
        }

        public LicenseAccessResult Check(LicenseOperation operation)
        {
            if (_mode != LicenseEnforcementMode.Enforced)
                return Allowed("LICENSE_ENFORCEMENT_NOT_ACTIVE");

            if (IsBaseAccessOperation(operation))
                return Allowed("LICENSE_BASE_ACCESS");

            LicenseObservationSnapshot snapshot = _snapshotStore.Current;
            if (snapshot == null)
                return new LicenseAccessResult(
                    false,
                    "LICENSE_DECISION_PENDING",
                    "Заявка на лицензирование ещё не подтверждена.");

            string currentClientId = _currentClientIdProvider?.Invoke();
            if (!string.IsNullOrWhiteSpace(currentClientId) &&
                !string.Equals(currentClientId, snapshot.ClientId, StringComparison.Ordinal))
            {
                return new LicenseAccessResult(
                    false,
                    "LICENSE_CLIENT_CONTEXT_MISMATCH",
                    "Лицензия ещё не проверена для выбранной торговой точки.");
            }

            IReadOnlyCollection<LicenseFeature> allowed = GetAllowedFeatures(snapshot);
            return IsGranted(allowed, operation)
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
            LicenseOperation requested)
        {
            return requested switch
            {
                LicenseOperation.ViewPointStatus or
                LicenseOperation.ManageServices or
                LicenseOperation.RecoverLmServices or
                LicenseOperation.InitializeLm or
                LicenseOperation.OpenLocalTools =>
                    Contains(features, LicenseFeature.ViewAndRepair),
                LicenseOperation.InstallComponents or
                LicenseOperation.ReinstallComponents or
                LicenseOperation.RestoreLmDatabase =>
                    Contains(features, LicenseFeature.InstallAndMaintenance),
                _ => false
            };
        }

        private static bool IsBaseAccessOperation(LicenseOperation operation) =>
            operation == LicenseOperation.CollectDiagnostics ||
            operation == LicenseOperation.SendDiagnostics ||
            operation == LicenseOperation.RequestHelp ||
            operation == LicenseOperation.InstallRuDesktop ||
            operation == LicenseOperation.ConfigureRuDesktop;

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
