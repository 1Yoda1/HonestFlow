using System;
using System.Collections.Generic;
using System.Linq;
using HonestFlow.Application.Installation;

namespace HonestFlow.Application.PointStatus
{
    public enum DiagnosticState { Healthy, Failed, Unknown }
    public enum DiagnosticConnectionState { Connected, Disconnected, Unknown }
    public enum WorkState { Ready, Attention, WorkImpossible, UnableToVerify }

    public sealed class DiagnosticComponentFact
    {
        public DiagnosticComponentFact(DiagnosticState state, string details) { State = state; Details = details ?? string.Empty; }
        public DiagnosticState State { get; }
        public string Details { get; }
    }

    public sealed class DiagnosticConnectionFact
    {
        public DiagnosticConnectionFact(DiagnosticConnectionState state, string details) { State = state; Details = details ?? string.Empty; }
        public DiagnosticConnectionState State { get; }
        public string Details { get; }
    }

    public sealed class DiagnosticsSnapshot
    {
        public DiagnosticComponentFact Esm { get; init; }
        public DiagnosticComponentFact Kkt { get; init; }
        public DiagnosticComponentFact Gismt { get; init; }
        public DiagnosticComponentFact Lm { get; init; }
        public DiagnosticComponentFact Controller { get; init; }
        public DiagnosticConnectionFact GismtToEsm { get; init; }
        public DiagnosticConnectionFact EsmToKkt { get; init; }
        public DiagnosticConnectionFact EsmToController { get; init; }
        public DiagnosticConnectionFact LmConnection { get; init; }
        public IReadOnlyList<ComponentVersionStatus> Versions { get; init; } = Array.Empty<ComponentVersionStatus>();
        public IReadOnlyList<DiagnosticFact> Facts { get; init; } = Array.Empty<DiagnosticFact>();
        public IReadOnlyList<DiagnosticIssue> Issues { get; init; } = Array.Empty<DiagnosticIssue>();
        public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> UserMessages { get; init; } = Array.Empty<string>();
        public bool GisPathAvailable { get; init; }
        public bool LmPathAvailable { get; init; }
        public DateTimeOffset ObservedAtUtc { get; init; }
        public WorkState WorkState { get; init; }
    }

    public sealed class DiagnosticsSnapshotBuilder
    {
        private static readonly Version MinimumAtolVersion = new(ComponentVersionRequirements.MinimumSupportedAtolDriver);

        public DiagnosticsSnapshot Create(
            PointStatusResult result,
            IReadOnlyList<ComponentVersionStatus> versionStatuses = null)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            IReadOnlyList<ComponentVersionStatus> versions = versionStatuses ?? Array.Empty<ComponentVersionStatus>();
            EsmStatusDto status = result.EsmApiStatus?.Status;
            EsmStatusDto api = status?.Software?.Data ?? status?.Data?.Software?.Data ?? status?.Data ?? status;
            DiagnosticComponentFact esm = Component(
                result.EsmApiStatus?.Kind == EsmStatusResultKind.Success &&
                result.EsmRegistration?.Kind == EsmRegistrationResultKind.Registered &&
                result.Esm?.Level != NodeLevel.Error,
                result.EsmApiStatus == null || result.EsmApiStatus.Kind != EsmStatusResultKind.Success
                    ? "Локальный API ЕСМ недоступен."
                    : result.EsmRegistration?.Kind != EsmRegistrationResultKind.Registered
                        ? "ЕСМ не зарегистрирован."
                        : result.Esm?.Details);
            DiagnosticComponentFact kkt = Component(
                IsAtolVersionReady(result.AtolDriverVersion) && HasRunningService(result.Kkt, "atol-grpc-service"),
                IsAtolVersionReady(result.AtolDriverVersion)
                    ? "Служба atol-grpc-service отсутствует или остановлена."
                    : "Драйвер ККТ отсутствует или его версия ниже 10.10.8.23.");
            DiagnosticComponentFact lm = FromNode(result.Lm);
            DiagnosticComponentFact controller = FromNode(result.Controller);
            DiagnosticComponentFact gismt = ComponentFromCode(api?.Gismt, "GIS MT");

            DiagnosticConnectionFact gisLink = FromCode(api?.Gismt, "GIS MT ↔ ЕСМ");
            DiagnosticConnectionFact esmKkt = FromCashRegister(result.CashRegister, result.KktPnP);
            DiagnosticConnectionFact esmController = FromCode(api?.LmController, "ЕСМ ↔ Controller");
            DiagnosticConnectionFact lmConnection = FromCode(api?.Lm, "Связь с ЛМ");

            if (esm.State == DiagnosticState.Failed)
            {
                gisLink = Unknown("Связь не проверена: ЕСМ недоступен.");
                esmKkt = Unknown("Связь не проверена: ЕСМ недоступен.");
                esmController = Unknown("Связь не проверена: ЕСМ недоступен.");
                lmConnection = Unknown("Связь не проверена: ЕСМ недоступен.");
            }

            bool gisPathAvailable = gismt.State == DiagnosticState.Healthy;
            bool lmPathAvailable = lm.State == DiagnosticState.Healthy &&
                esmController.State == DiagnosticConnectionState.Connected &&
                lmConnection.State == DiagnosticConnectionState.Connected;

            var reasons = new List<string>();
            AddFailed(reasons, esm, "ЕСМ");
            AddFailed(reasons, kkt, "ККТ");
            AddDisconnected(reasons, esmKkt, "Связь ЕСМ ↔ ККТ");
            if (controller.State == DiagnosticState.Failed)
                reasons.Add("Контроллер: " + controller.Details);
            if (gismt.State != DiagnosticState.Healthy)
                reasons.Add("GIS MT: " + gismt.Details);
            if (lm.State != DiagnosticState.Healthy)
                reasons.Add("ЛМ ЧЗ: " + lm.Details);
            if (esmController.State != DiagnosticConnectionState.Connected)
                reasons.Add("ЕСМ ↔ Controller: " + esmController.Details);
            if (lmConnection.State != DiagnosticConnectionState.Connected)
                reasons.Add("Связь с ЛМ: " + lmConnection.Details);
            foreach (ComponentVersionStatus version in versions.Where(IsVersionIssue))
                reasons.Add(VersionDetails(version));

            var context = new DiagnosticEvaluationContext
            {
                Result = result,
                Versions = versions,
                Esm = esm,
                Kkt = kkt,
                Gismt = gismt,
                Lm = lm,
                Controller = controller,
                EsmKkt = esmKkt,
                EsmController = esmController,
                LmConnection = lmConnection,
                Api = api,
                GisPathAvailable = gisPathAvailable,
                LmPathAvailable = lmPathAvailable
            };
            IReadOnlyList<DiagnosticIssue> issues = DiagnosticIssueEvaluator.Evaluate(context);
            IReadOnlyList<DiagnosticFact> facts = DiagnosticFactsBuilder.Create(context);
            IReadOnlyList<string> userMessages = issues
                .Select(issue => issue.UserMessage)
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            WorkState workState = issues.Any(issue => issue.Severity == DiagnosticSeverity.WorkImpossible)
                ? WorkState.WorkImpossible
                : issues.Any(issue => issue.Severity == DiagnosticSeverity.UnableToVerify)
                    ? WorkState.UnableToVerify
                    : issues.Count > 0 ? WorkState.Attention : WorkState.Ready;

            return new DiagnosticsSnapshot
            {
                Esm = esm, Kkt = kkt, Gismt = gismt, Lm = lm, Controller = controller,
                GismtToEsm = gisLink, EsmToKkt = esmKkt,
                EsmToController = esmController, LmConnection = lmConnection,
                Versions = versions, Facts = facts, Issues = issues,
                Reasons = reasons, UserMessages = userMessages,
                GisPathAvailable = gisPathAvailable, LmPathAvailable = lmPathAvailable,
                ObservedAtUtc = DateTimeOffset.UtcNow,
                WorkState = workState
            };
        }

        private static DiagnosticComponentFact Component(bool healthy, string details) =>
            new(healthy ? DiagnosticState.Healthy : DiagnosticState.Failed, details);
        private static DiagnosticComponentFact FromNode(NodeStatus node) =>
            new(node?.Level == NodeLevel.Ok ? DiagnosticState.Healthy : node == null ? DiagnosticState.Unknown : DiagnosticState.Failed, node?.Details);
        private static DiagnosticComponentFact ComponentFromCode(EsmComponentStatus status, string name) =>
            status?.Code == null ? new(DiagnosticState.Unknown, $"{name}: данные отсутствуют.") :
            status.Code == 0 ? new(DiagnosticState.Healthy, $"{name}: код 0.") :
            new(DiagnosticState.Failed, $"{name}: код {status.Code}. {status.Error} {status.LastConnection}".Trim());
        private static DiagnosticConnectionFact FromCode(EsmComponentStatus status, string name) =>
            status?.Code == null ? Unknown($"{name}: данные отсутствуют.") :
            status.Code == 0 ? new(DiagnosticConnectionState.Connected, $"{name}: код 0.") :
            new(DiagnosticConnectionState.Disconnected, $"{name}: код {status.Code}. {status.Error}".Trim());
        private static DiagnosticConnectionFact FromCashRegister(EsmCashRegisterResult result, KktPnpResult pnp) => result?.Kind switch
        {
            EsmCashRegisterResultKind.Connected => new(DiagnosticConnectionState.Connected, "CashRegister.Data.kkt[] содержит подключённую ККТ."),
            EsmCashRegisterResultKind.Disconnected when pnp?.Kind == KktPnpResultKind.Detected =>
                new(DiagnosticConnectionState.Disconnected, "CashRegister.Data.kkt[] пуст; " + pnp.Details),
            EsmCashRegisterResultKind.Disconnected when pnp?.Kind == KktPnpResultKind.NotDetected =>
                new(DiagnosticConnectionState.Disconnected, "CashRegister.Data.kkt[] пуст; ATOL PnP device not found."),
            EsmCashRegisterResultKind.Disconnected => new(DiagnosticConnectionState.Disconnected, "CashRegister.Data.kkt[] пуст; PnP status unavailable."),
            _ => Unknown("CashRegister.Data.kkt[] не получен.")
        };
        private static DiagnosticConnectionFact Unknown(string details) => new(DiagnosticConnectionState.Unknown, details);
        private static bool HasRunningService(NodeStatus status, string name) => status?.Services?.Any(x => string.Equals(x.ServiceName, name, StringComparison.OrdinalIgnoreCase) && x.IsRunning) == true;
        private static bool IsAtolVersionReady(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string token = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return Version.TryParse(token, out Version version) && version >= MinimumAtolVersion;
        }
        private static void AddFailed(List<string> reasons, DiagnosticComponentFact fact, string name) { if (fact.State == DiagnosticState.Failed) reasons.Add(name + ": " + fact.Details); }
        private static void AddDisconnected(List<string> reasons, DiagnosticConnectionFact fact, string name) { if (fact.State == DiagnosticConnectionState.Disconnected) reasons.Add(name + ": " + fact.Details); }

        private static bool IsVersionIssue(ComponentVersionStatus version) =>
            version?.State == ComponentVersionState.UpdateRequired || version?.State == ComponentVersionState.BelowMinimum;

        private static string VersionDetails(ComponentVersionStatus version) =>
            $"{version.ComponentName}: installed={ValueOrDash(version.InstalledVersion)}; " +
            $"minimum={ValueOrDash(version.MinimumSupportedVersion)}; target={ValueOrDash(version.TargetVersion)}; state={version.State}.";

        private static string ValueOrDash(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
    }
}
