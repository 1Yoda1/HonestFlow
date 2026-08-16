using System;
using System.Collections.Generic;
using System.Linq;
using HonestFlow.Application.Installation;

namespace HonestFlow.Application.PointStatus
{
    public enum DiagnosticIssueCode
    {
        ESM_NOT_INSTALLED,
        ESM_API_UNAVAILABLE,
        ESM_NOT_REGISTERED,
        ESM_CM_SERVICE_STOPPED,
        ESM_ORCHESTRATOR_STOPPED,
        ATOL_DRIVER_MISSING,
        ATOL_DRIVER_TOO_OLD,
        ATOL_GRPC_SERVICE_STOPPED,
        KKT_NOT_DETECTED,
        KKT_NOT_VISIBLE_TO_ESM,
        LM_NOT_INSTALLED,
        LM_API_UNAVAILABLE,
        LM_SYNC_ERROR,
        LM_NOT_CONFIGURED,
        LM_INITIALIZING,
        LM_CONTROLLER_DISCONNECTED,
        GIS_MT_UNAVAILABLE,
        MARKING_CHANNEL_UNAVAILABLE,
        COMPONENT_VERSION_BELOW_MINIMUM,
        COMPONENT_TARGET_VERSION_MISMATCH
    }

    public enum DiagnosticComponent
    {
        Esm,
        Kkt,
        ClientSoftware,
        Lm,
        Controller,
        GisMt,
        MarkingChannel,
        ComponentVersion
    }

    public enum DiagnosticSeverity { Attention, UnableToVerify, WorkImpossible }

    public enum DiagnosticFixKey
    {
        RestartEsm,
        StartEsmServices,
        StartAtolGrpcService,
        RestartLm,
        RepairLmSync,
        InitializeLm,
        RepairKktConnection
    }

    public enum DiagnosticFactState { Success, Failure, Unknown }

    public sealed class DiagnosticEvidence
    {
        public DiagnosticEvidence(string key, string value) { Key = key; Value = value ?? string.Empty; }
        public string Key { get; }
        public string Value { get; }
    }

    public sealed class DiagnosticFact
    {
        public DiagnosticFact(string key, DiagnosticComponent component, DiagnosticFactState state, string value, string evidence = null)
        {
            Key = key;
            Component = component;
            State = state;
            Value = value ?? string.Empty;
            Evidence = evidence ?? string.Empty;
        }

        public string Key { get; }
        public DiagnosticComponent Component { get; }
        public DiagnosticFactState State { get; }
        public string Value { get; }
        public string Evidence { get; }
    }

    public sealed class DiagnosticIssue
    {
        public DiagnosticIssue(
            DiagnosticIssueCode code,
            DiagnosticComponent component,
            DiagnosticSeverity severity,
            string title,
            string userMessage,
            string technicalDetails,
            IReadOnlyList<DiagnosticEvidence> evidence,
            DiagnosticFixKey? suggestedFix = null)
        {
            Code = code;
            Component = component;
            Severity = severity;
            Title = title ?? string.Empty;
            UserMessage = userMessage ?? string.Empty;
            TechnicalDetails = technicalDetails ?? string.Empty;
            Evidence = evidence ?? Array.Empty<DiagnosticEvidence>();
            SuggestedFix = suggestedFix;
        }

        public DiagnosticIssueCode Code { get; }
        public DiagnosticComponent Component { get; }
        public DiagnosticSeverity Severity { get; }
        public string Title { get; }
        public string UserMessage { get; }
        public string TechnicalDetails { get; }
        public IReadOnlyList<DiagnosticEvidence> Evidence { get; }
        public DiagnosticFixKey? SuggestedFix { get; }

        public string ToStructuredLog() =>
            $"Event=DiagnosticIssue Code={Code} Severity={Severity} Component={Component} FixKey={SuggestedFix?.ToString() ?? "none"}";
    }

    internal sealed class DiagnosticEvaluationContext
    {
        public PointStatusResult Result { get; init; }
        public IReadOnlyList<ComponentVersionStatus> Versions { get; init; }
        public DiagnosticComponentFact Esm { get; init; }
        public DiagnosticComponentFact Kkt { get; init; }
        public DiagnosticComponentFact Gismt { get; init; }
        public DiagnosticComponentFact Lm { get; init; }
        public DiagnosticComponentFact Controller { get; init; }
        public DiagnosticConnectionFact EsmKkt { get; init; }
        public DiagnosticConnectionFact EsmController { get; init; }
        public DiagnosticConnectionFact LmConnection { get; init; }
        public EsmStatusDto Api { get; init; }
        public bool GisPathAvailable { get; init; }
        public bool LmPathAvailable { get; init; }
    }

    internal static class DiagnosticIssueEvaluator
    {
        public static IReadOnlyList<DiagnosticIssue> Evaluate(DiagnosticEvaluationContext context)
        {
            var issues = new List<DiagnosticIssue>();
            PointStatusResult result = context.Result;
            ServiceSnapshot orchestrator = FindService(result, "esm-orchestrator");
            ServiceSnapshot cm = FindServicePrefix(result, "esm-cm-");
            ServiceSnapshot atolGrpc = FindService(result, "atol-grpc-service");

            if (orchestrator == null && cm == null)
                Add(issues, Issue(DiagnosticIssueCode.ESM_NOT_INSTALLED, DiagnosticComponent.Esm, DiagnosticSeverity.WorkImpossible,
                    "ТС ПИоТ не установлен", "Компоненты ТС ПИоТ не найдены.", context.Esm.Details, DiagnosticFixKey.StartEsmServices));
            if (orchestrator != null && !orchestrator.IsRunning)
                Add(issues, Issue(DiagnosticIssueCode.ESM_ORCHESTRATOR_STOPPED, DiagnosticComponent.Esm, DiagnosticSeverity.WorkImpossible,
                    "Служба ТС ПИоТ остановлена", "Служба ESM Orchestrator не запущена.", context.Esm.Details, DiagnosticFixKey.StartEsmServices,
                    Evidence("Service", orchestrator.ServiceName), Evidence("State", orchestrator.State)));
            if (cm != null && !cm.IsRunning)
                Add(issues, Issue(DiagnosticIssueCode.ESM_CM_SERVICE_STOPPED, DiagnosticComponent.Esm, DiagnosticSeverity.WorkImpossible,
                    "Служба ТС ПИоТ остановлена", "Служба ESM CM не запущена.", context.Esm.Details, DiagnosticFixKey.StartEsmServices,
                    Evidence("Service", cm.ServiceName), Evidence("State", cm.State)));
            if (result.EsmApiStatus == null || result.EsmApiStatus.Kind == EsmStatusResultKind.Unavailable)
                Add(issues, Issue(DiagnosticIssueCode.ESM_API_UNAVAILABLE, DiagnosticComponent.Esm, DiagnosticSeverity.WorkImpossible,
                    "ТС ПИоТ недоступен", "Локальный API ТС ПИоТ не отвечает.", context.Esm.Details, DiagnosticFixKey.RestartEsm,
                    Evidence("Probe", "EsmRest"), Evidence("Endpoint", "http://127.0.0.1:51077")));
            if (result.EsmRegistration?.Kind == EsmRegistrationResultKind.NotConfigured)
                Add(issues, Issue(DiagnosticIssueCode.ESM_NOT_REGISTERED, DiagnosticComponent.Esm, DiagnosticSeverity.WorkImpossible,
                    "ТС ПИоТ не зарегистрирован", "Экземпляр ТС ПИоТ требует регистрации.", context.Esm.Details));

            AddAtolIssues(issues, context, atolGrpc);
            AddLmIssues(issues, context);

            if (!context.GisPathAvailable)
                Add(issues, Issue(DiagnosticIssueCode.GIS_MT_UNAVAILABLE, DiagnosticComponent.GisMt,
                    context.LmPathAvailable ? DiagnosticSeverity.Attention : DiagnosticSeverity.WorkImpossible,
                    "ГИС МТ недоступна", context.LmPathAvailable
                        ? "ГИС МТ недоступна. Проверка выполняется через ЛМ ЧЗ."
                        : "ГИС МТ недоступна.", context.Gismt.Details,
                    evidence: new[] { Evidence("gismt.code", context.Api?.Gismt?.Code?.ToString() ?? "missing") }));

            if (!context.LmPathAvailable &&
                (context.Controller.State != DiagnosticState.Healthy ||
                 context.EsmController.State != DiagnosticConnectionState.Connected ||
                 context.LmConnection.State != DiagnosticConnectionState.Connected))
                Add(issues, Issue(DiagnosticIssueCode.LM_CONTROLLER_DISCONNECTED, DiagnosticComponent.Controller,
                    context.GisPathAvailable ? DiagnosticSeverity.Attention : DiagnosticSeverity.WorkImpossible,
                    "Контроллер не видит ЛМ ЧЗ", "Контроллер не видит ЛМ ЧЗ.",
                    context.EsmController.Details + Environment.NewLine + context.LmConnection.Details));

            if (!context.GisPathAvailable && !context.LmPathAvailable)
                Add(issues, Issue(DiagnosticIssueCode.MARKING_CHANNEL_UNAVAILABLE, DiagnosticComponent.MarkingChannel,
                    DiagnosticSeverity.WorkImpossible, "Проверка маркировки недоступна",
                    "Нет доступного канала проверки маркировки.",
                    $"GisPathAvailable={context.GisPathAvailable}; LmPathAvailable={context.LmPathAvailable}"));

            foreach (ComponentVersionStatus version in context.Versions ?? Array.Empty<ComponentVersionStatus>())
            {
                if (version.State == ComponentVersionState.BelowMinimum)
                    Add(issues, Issue(DiagnosticIssueCode.COMPONENT_VERSION_BELOW_MINIMUM, DiagnosticComponent.ComponentVersion,
                        DiagnosticSeverity.WorkImpossible, "Версия компонента не поддерживается",
                        $"Версия компонента «{version.ComponentName}» ниже минимально поддерживаемой.", VersionEvidence(version)));
                else if (version.State == ComponentVersionState.UpdateRequired)
                    Add(issues, Issue(DiagnosticIssueCode.COMPONENT_TARGET_VERSION_MISMATCH, DiagnosticComponent.ComponentVersion,
                        DiagnosticSeverity.Attention, "Доступно обновление компонента",
                        $"Для компонента «{version.ComponentName}» настроена другая версия.", VersionEvidence(version)));
            }

            return issues;
        }

        private static void AddAtolIssues(List<DiagnosticIssue> issues, DiagnosticEvaluationContext context, ServiceSnapshot atolGrpc)
        {
            string version = context.Result.AtolDriverVersion;
            if (string.IsNullOrWhiteSpace(version) || version.Contains("не установлен", StringComparison.OrdinalIgnoreCase))
                Add(issues, Issue(DiagnosticIssueCode.ATOL_DRIVER_MISSING, DiagnosticComponent.Kkt, DiagnosticSeverity.WorkImpossible,
                    "Драйвер ККТ не установлен", "Драйвер АТОЛ не найден.", version));
            else if (!IsAtolSupported(version))
                Add(issues, Issue(DiagnosticIssueCode.ATOL_DRIVER_TOO_OLD, DiagnosticComponent.Kkt, DiagnosticSeverity.WorkImpossible,
                    "Версия драйвера ККТ не поддерживается", "Обновите драйвер АТОЛ.", version));

            if (atolGrpc == null || !atolGrpc.IsRunning)
                Add(issues, Issue(DiagnosticIssueCode.ATOL_GRPC_SERVICE_STOPPED, DiagnosticComponent.Kkt, DiagnosticSeverity.WorkImpossible,
                    "Служба ККТ остановлена", "Служба atol-grpc-service не запущена.", context.Kkt.Details,
                    DiagnosticFixKey.StartAtolGrpcService,
                    Evidence("State", atolGrpc?.State ?? "missing")));

            if (context.Result.CashRegister?.Kind == EsmCashRegisterResultKind.Disconnected)
            {
                bool detected = context.Result.KktPnP?.Kind == KktPnpResultKind.Detected;
                Add(issues, Issue(detected ? DiagnosticIssueCode.KKT_NOT_VISIBLE_TO_ESM : DiagnosticIssueCode.KKT_NOT_DETECTED,
                    DiagnosticComponent.Kkt, DiagnosticSeverity.WorkImpossible,
                    detected ? "ТС ПИоТ не видит ККТ" : "ККТ не обнаружена",
                    detected ? "ККТ обнаружена Windows, но ТС ПИоТ её не видит." : "ККТ не обнаружена.",
                    context.EsmKkt.Details, detected ? DiagnosticFixKey.RepairKktConnection : null,
                    Evidence("PnP", context.Result.KktPnP?.Kind.ToString() ?? "Unknown")));
            }
        }

        private static void AddLmIssues(List<DiagnosticIssue> issues, DiagnosticEvaluationContext context)
        {
            LmDiagnosticProbeState state = context.Result.LmProbe?.State ??
                (context.Lm.State == DiagnosticState.Healthy ? LmDiagnosticProbeState.Available : LmDiagnosticProbeState.ApiUnavailable);
            if (state == LmDiagnosticProbeState.Available) return;
            DiagnosticSeverity severity = context.GisPathAvailable ? DiagnosticSeverity.Attention : DiagnosticSeverity.WorkImpossible;
            DiagnosticIssue issue = state switch
            {
                LmDiagnosticProbeState.NotInstalled => Issue(DiagnosticIssueCode.LM_NOT_INSTALLED, DiagnosticComponent.Lm, severity,
                    "ЛМ ЧЗ не установлен", "ЛМ ЧЗ не найден.", context.Lm.Details),
                LmDiagnosticProbeState.SyncError => Issue(DiagnosticIssueCode.LM_SYNC_ERROR, DiagnosticComponent.Lm, severity,
                    "Ошибка синхронизации ЛМ ЧЗ", "ЛМ ЧЗ недоступен. Проверка выполняется через ГИС МТ.", context.Lm.Details, DiagnosticFixKey.RepairLmSync),
                LmDiagnosticProbeState.NotConfigured => Issue(DiagnosticIssueCode.LM_NOT_CONFIGURED, DiagnosticComponent.Lm, severity,
                    "ЛМ ЧЗ не настроен", "ЛМ ЧЗ требует настройки.", context.Lm.Details, DiagnosticFixKey.InitializeLm),
                LmDiagnosticProbeState.Initializing => Issue(DiagnosticIssueCode.LM_INITIALIZING, DiagnosticComponent.Lm, severity,
                    "ЛМ ЧЗ инициализируется", "Инициализация ЛМ ЧЗ ещё не завершена.", context.Lm.Details),
                _ => Issue(DiagnosticIssueCode.LM_API_UNAVAILABLE, DiagnosticComponent.Lm, severity,
                    "ЛМ ЧЗ недоступен", context.GisPathAvailable
                        ? "ЛМ ЧЗ недоступен. Проверка выполняется через ГИС МТ."
                        : "API ЛМ ЧЗ не отвечает.", context.Lm.Details, DiagnosticFixKey.RestartLm)
            };
            Add(issues, issue);
        }

        private static DiagnosticIssue Issue(
            DiagnosticIssueCode code, DiagnosticComponent component, DiagnosticSeverity severity,
            string title, string userMessage, string technicalDetails, DiagnosticFixKey? fix = null,
            params DiagnosticEvidence[] evidence) =>
            new(code, component, severity, title, userMessage, technicalDetails, evidence, fix);

        private static void Add(List<DiagnosticIssue> issues, DiagnosticIssue issue)
        {
            if (issues.All(existing => existing.Code != issue.Code)) issues.Add(issue);
        }

        private static DiagnosticEvidence Evidence(string key, string value) => new(key, value);
        private static ServiceSnapshot FindService(PointStatusResult result, string name) =>
            AllServices(result).FirstOrDefault(x => string.Equals(x.ServiceName, name, StringComparison.OrdinalIgnoreCase));
        private static ServiceSnapshot FindServicePrefix(PointStatusResult result, string prefix) =>
            AllServices(result).FirstOrDefault(x => x.ServiceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        private static IEnumerable<ServiceSnapshot> AllServices(PointStatusResult result) =>
            (result.ServiceSnapshots ?? Array.Empty<ServiceSnapshot>())
            .Concat(result.Esm?.Services ?? Array.Empty<ServiceSnapshot>())
            .Concat(result.Kkt?.Services ?? Array.Empty<ServiceSnapshot>())
            .Concat(result.Lm?.Services ?? Array.Empty<ServiceSnapshot>())
            .Concat(result.Controller?.Services ?? Array.Empty<ServiceSnapshot>())
            .GroupBy(x => x.ServiceName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
        private static bool IsAtolSupported(string value)
        {
            string token = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return Version.TryParse(token, out Version actual) &&
                Version.TryParse(ComponentVersionRequirements.MinimumSupportedAtolDriver, out Version minimum) && actual >= minimum;
        }
        private static string VersionEvidence(ComponentVersionStatus version) =>
            $"installed={version.InstalledVersion ?? "-"}; minimum={version.MinimumSupportedVersion ?? "-"}; target={version.TargetVersion ?? "-"}";
    }

    internal static class DiagnosticFactsBuilder
    {
        public static IReadOnlyList<DiagnosticFact> Create(DiagnosticEvaluationContext context)
        {
            var facts = new List<DiagnosticFact>();
            foreach (ServiceSnapshot service in (context.Result.ServiceSnapshots ?? Array.Empty<ServiceSnapshot>())
                         .OrderBy(service => service.ServiceName, StringComparer.OrdinalIgnoreCase))
                facts.Add(new DiagnosticFact("Service." + service.ServiceName, ServiceComponent(service.ServiceName),
                    service.IsRunning ? DiagnosticFactState.Success : DiagnosticFactState.Failure,
                    service.State, "Windows ServiceController"));

            foreach (ComponentVersionStatus version in context.Versions ?? Array.Empty<ComponentVersionStatus>())
                facts.Add(new DiagnosticFact("Version." + version.ComponentName, DiagnosticComponent.ComponentVersion,
                    version.State is ComponentVersionState.Current ? DiagnosticFactState.Success :
                    version.State is ComponentVersionState.Unknown ? DiagnosticFactState.Unknown : DiagnosticFactState.Failure,
                    version.InstalledVersion ?? "not installed", VersionEvidence(version)));

            EsmComponentStatus client = context.Api?.ClientSoftware;
            facts.Add(new DiagnosticFact("Esm.Api", DiagnosticComponent.Esm,
                context.Result.EsmApiStatus?.Kind == EsmStatusResultKind.Success ? DiagnosticFactState.Success : DiagnosticFactState.Failure,
                context.Result.EsmApiStatus?.Kind.ToString() ?? "not requested", context.Esm.Details));
            facts.Add(new DiagnosticFact("Esm.Registration", DiagnosticComponent.Esm,
                context.Result.EsmRegistration?.Kind == EsmRegistrationResultKind.Registered ? DiagnosticFactState.Success : DiagnosticFactState.Failure,
                context.Result.EsmRegistration?.Kind.ToString() ?? "unknown"));
            facts.Add(new DiagnosticFact("Kkt.Live", DiagnosticComponent.Kkt,
                context.EsmKkt.State == DiagnosticConnectionState.Connected ? DiagnosticFactState.Success : DiagnosticFactState.Failure,
                context.Result.CashRegister?.Kind.ToString() ?? "unknown", context.EsmKkt.Details));
            facts.Add(new DiagnosticFact("Kkt.PnP", DiagnosticComponent.Kkt,
                context.Result.KktPnP?.Kind == KktPnpResultKind.Detected ? DiagnosticFactState.Success :
                context.Result.KktPnP?.Kind == KktPnpResultKind.NotDetected ? DiagnosticFactState.Failure : DiagnosticFactState.Unknown,
                context.Result.KktPnP?.Kind.ToString() ?? "unknown", context.Result.KktPnP?.Details));
            facts.Add(new DiagnosticFact("ClientSoftware", DiagnosticComponent.ClientSoftware,
                DiagnosticFactState.Unknown, client?.Name ?? "not detected", ClientSoftwareEvidence(client)));
            facts.Add(new DiagnosticFact("Lm.Health", DiagnosticComponent.Lm,
                context.Lm.State == DiagnosticState.Healthy ? DiagnosticFactState.Success :
                context.Lm.State == DiagnosticState.Failed ? DiagnosticFactState.Failure : DiagnosticFactState.Unknown,
                context.Result.LmProbe?.State.ToString() ?? context.Lm.State.ToString(), context.Lm.Details));
            facts.Add(ConnectionFact("Lm.Controller", DiagnosticComponent.Controller, context.EsmController));
            facts.Add(ConnectionFact("Lm.Connection", DiagnosticComponent.Lm, context.LmConnection));
            facts.Add(new DiagnosticFact("GisMt", DiagnosticComponent.GisMt,
                context.Gismt.State == DiagnosticState.Healthy ? DiagnosticFactState.Success :
                context.Gismt.State == DiagnosticState.Failed ? DiagnosticFactState.Failure : DiagnosticFactState.Unknown,
                context.Api?.Gismt?.Code?.ToString() ?? "unknown", context.Gismt.Details));
            return facts;
        }

        private static DiagnosticFact ConnectionFact(string key, DiagnosticComponent component, DiagnosticConnectionFact fact) =>
            new(key, component,
                fact.State == DiagnosticConnectionState.Connected ? DiagnosticFactState.Success :
                fact.State == DiagnosticConnectionState.Disconnected ? DiagnosticFactState.Failure : DiagnosticFactState.Unknown,
                fact.State.ToString(), fact.Details);

        private static DiagnosticComponent ServiceComponent(string serviceName) =>
            serviceName.StartsWith("atol", StringComparison.OrdinalIgnoreCase) ||
            serviceName.StartsWith("uem-", StringComparison.OrdinalIgnoreCase)
                ? DiagnosticComponent.Kkt
                : serviceName.Equals("regime", StringComparison.OrdinalIgnoreCase) ||
                  serviceName.Equals("yenisei", StringComparison.OrdinalIgnoreCase)
                    ? DiagnosticComponent.Lm
                    : serviceName.Equals("esm-lm-controller", StringComparison.OrdinalIgnoreCase)
                        ? DiagnosticComponent.Controller
                        : DiagnosticComponent.Esm;

        private static string VersionEvidence(ComponentVersionStatus version) =>
            $"minimum={version.MinimumSupportedVersion ?? "-"}; target={version.TargetVersion ?? "-"}; state={version.State}";

        private static string ClientSoftwareEvidence(EsmComponentStatus client) =>
            $"name={client?.Name ?? "-"}; version={client?.Version ?? "-"}; id={client?.Id ?? "-"}; " +
            $"code={client?.Code?.ToString() ?? "-"}; error={client?.Error ?? "-"}; lastConnection={client?.LastConnection ?? "-"}";
    }
}
