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
        public DiagnosticsSnapshot Create(
            PointStatusResult result,
            IReadOnlyList<ComponentVersionStatus> versionStatuses = null)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            IReadOnlyList<ComponentVersionStatus> versions = versionStatuses ?? Array.Empty<ComponentVersionStatus>();
            EsmStatusDto status = result.EsmApiStatus?.Status;
            EsmStatusDto api = status?.Software?.Data ?? status?.Data?.Software?.Data ?? status?.Data ?? status;
            DiagnosticComponentFact esm = FromEsmNode(result.Esm);
            DiagnosticComponentFact kkt = FromKktNode(result.Kkt);
            DiagnosticComponentFact lm = FromLmProbe(result.LmProbe, result.Lm);
            DiagnosticComponentFact controller = FromControllerNode(result.Controller);
            DiagnosticComponentFact gismt = ComponentFromCode(api?.Gismt, "GIS MT");

            DiagnosticConnectionFact gisLink = FromCode(api?.Gismt, "GIS MT ↔ ЕСМ");
            DiagnosticConnectionFact esmKkt = FromCashRegister(
                result.EsmApiStatus,
                result.EsmRegistration,
                result.CashRegister);
            DiagnosticConnectionFact esmController = FromLmInfoCode(result.EsmApiStatus?.Status?.LmInfo, "ЕСМ ↔ Controller");
            DiagnosticConnectionFact lmConnection = FromCode(api?.Lm, "Связь с ЛМ");

            if (esm.State == DiagnosticState.Failed)
            {
                gisLink = Unknown("Связь не проверена: ЕСМ недоступен.");
                esmKkt = Unknown("Связь не проверена: ЕСМ недоступен.");
                esmController = Unknown("Связь не проверена: ЕСМ недоступен.");
                lmConnection = Unknown("Связь не проверена: ЕСМ недоступен.");
            }

            bool gisPathAvailable = gismt.State == DiagnosticState.Healthy;
            bool lmPathAvailable = IsLmTechnicallyAvailable(result.LmProbe, lm) &&
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
        private static DiagnosticComponentFact FromControllerNode(NodeStatus node) =>
            new(node?.Level == NodeLevel.Ok ? DiagnosticState.Healthy :
                node?.Level == NodeLevel.Warning ? DiagnosticState.Unknown :
                node == null ? DiagnosticState.Unknown : DiagnosticState.Failed,
                node?.Details);
        private static DiagnosticComponentFact FromKktNode(NodeStatus node) =>
            new(node?.Level == NodeLevel.Ok ? DiagnosticState.Healthy :
                node?.Level == NodeLevel.Warning ? DiagnosticState.Unknown :
                node == null ? DiagnosticState.Unknown : DiagnosticState.Failed,
                node?.Details);
        private static DiagnosticComponentFact FromEsmNode(NodeStatus node) =>
            new(node?.Level == NodeLevel.Ok ? DiagnosticState.Healthy :
                node?.Level == NodeLevel.Warning ? DiagnosticState.Unknown :
                node == null ? DiagnosticState.Unknown : DiagnosticState.Failed,
                node?.Details);
        private static DiagnosticComponentFact FromLmProbe(LmDiagnosticProbeResult probe, NodeStatus node) =>
            probe == null
                ? FromNode(node)
                : new DiagnosticComponentFact(
                    probe.State == LmDiagnosticProbeState.Available && probe.HealthAvailable
                        ? DiagnosticState.Healthy
                        : probe.State == LmDiagnosticProbeState.Unknown
                            ? DiagnosticState.Unknown
                            : DiagnosticState.Failed,
                    node?.Details);
        private static bool IsLmTechnicallyAvailable(LmDiagnosticProbeResult probe, DiagnosticComponentFact lm) =>
            probe == null
                ? lm.State == DiagnosticState.Healthy
                : probe.State == LmDiagnosticProbeState.Available && probe.HealthAvailable;
        private static DiagnosticComponentFact ComponentFromCode(EsmComponentStatus status, string name) =>
            status?.Code == null ? new(DiagnosticState.Unknown, $"{name}: данные отсутствуют.") :
            status.Code == 0 ? new(DiagnosticState.Healthy, $"{name}: код 0.") :
            new(DiagnosticState.Failed, $"{name}: код {status.Code}. {status.Error} {status.LastConnection}".Trim());
        private static DiagnosticConnectionFact FromCode(EsmComponentStatus status, string name) =>
            status?.Code == null ? Unknown($"{name}: данные отсутствуют.") :
            status.Code == 0 ? new(DiagnosticConnectionState.Connected, $"{name}: код 0.") :
            new(DiagnosticConnectionState.Disconnected, $"{name}: код {status.Code}. {status.Error}".Trim());
        private static DiagnosticConnectionFact FromLmInfoCode(EsmLmInfoDto status, string name) =>
            status?.EffectiveCode == null ? Unknown($"{name}: данные отсутствуют.") :
            status.EffectiveCode == 0 ? new(DiagnosticConnectionState.Connected, $"{name}: код 0.") :
            new(DiagnosticConnectionState.Disconnected, $"{name}: код {status.EffectiveCode}.");
        private static DiagnosticConnectionFact FromCashRegister(
            EsmStatusResult esmApi,
            EsmRegistrationResult registration,
            EsmCashRegisterResult result)
        {
            if (esmApi?.Kind != EsmStatusResultKind.Success)
                return Unknown("Связь не проверена: API ЕСМ недоступен.");
            if (registration?.Kind != EsmRegistrationResultKind.Registered)
                return Unknown("Связь не проверена: ЕСМ не зарегистрирован.");
            return result?.Kind switch
            {
            EsmCashRegisterResultKind.Connected => new(DiagnosticConnectionState.Connected, "CashRegister.Data.kkt[] содержит подключённую ККТ."),
            EsmCashRegisterResultKind.Disconnected => new(DiagnosticConnectionState.Disconnected, "CashRegister.Data.kkt[] пуст."),
            _ => Unknown("CashRegister.Data.kkt[] не получен.")
        };
        }
        private static DiagnosticConnectionFact Unknown(string details) => new(DiagnosticConnectionState.Unknown, details);
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
