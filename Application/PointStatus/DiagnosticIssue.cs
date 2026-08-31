using System;
using System.Collections.Generic;
using System.Linq;
using HonestFlow.Application.Installation;

namespace HonestFlow.Application.PointStatus
{
    public enum DiagnosticIssueCode
    {
        ESM_NOT_INSTALLED,
        ESM_SERVICE_MISSING,
        ESM_API_UNAVAILABLE,
        ESM_REGISTRATION_STATUS_UNAVAILABLE,
        ESM_NOT_REGISTERED,
        ESM_CM_SERVICE_STOPPED,
        ESM_ORCHESTRATOR_STOPPED,
        ATOL_DRIVER_MISSING,
        ATOL_DRIVER_TOO_OLD,
        ATOL_GRPC_SERVICE_STOPPED,
        KKT_NOT_DETECTED,
        KKT_NOT_VISIBLE_TO_ESM,
        KKT_SERVICE_MISSING,
        LM_NOT_INSTALLED,
        LM_API_UNAVAILABLE,
        LM_SYNC_ERROR,
        LM_NOT_CONFIGURED,
        LM_INITIALIZING,
        LM_INN_MISMATCH,
        ATOL_DRIVER_ARCHITECTURE_MISMATCH,
        LM_STATUS_UNKNOWN,
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
        RunSmartInstallation,
        RestartEsm,
        StartEsmServices,
        RegisterTsPiot,
        StartKktServices,
        RestartLm,
        RepairLmSync,
        InitializeLm,
        ConfirmLmClientMismatch,
        RestartLmController
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
            bool hasEsmServiceEvidence = result.EsmServiceStatus != null ||
                result.ServiceSnapshots?.Any(service =>
                    string.Equals(service.ServiceName, "esm-orchestrator", StringComparison.OrdinalIgnoreCase) ||
                    service.ServiceName.StartsWith("esm-cm-", StringComparison.OrdinalIgnoreCase)) == true;

            if (string.Equals(result.Esm?.StatusText, "ЕСМ не установлен", StringComparison.Ordinal))
                Add(issues, Issue(DiagnosticIssueCode.ESM_NOT_INSTALLED, DiagnosticComponent.Esm, DiagnosticSeverity.WorkImpossible,
                    "ТС ПИоТ не установлен", "Компоненты ТС ПИоТ не найдены.", context.Esm.Details, DiagnosticFixKey.RunSmartInstallation));
            else if (hasEsmServiceEvidence && orchestrator == null)
                Add(issues, Issue(DiagnosticIssueCode.ESM_SERVICE_MISSING, DiagnosticComponent.Esm, DiagnosticSeverity.WorkImpossible,
                    "Служба ТС ПИоТ не найдена", "Не найдена обязательная служба ТС ПИоТ.", context.Esm.Details, DiagnosticFixKey.RunSmartInstallation));
            else if (orchestrator != null && !orchestrator.IsRunning)
                Add(issues, Issue(DiagnosticIssueCode.ESM_ORCHESTRATOR_STOPPED, DiagnosticComponent.Esm, DiagnosticSeverity.WorkImpossible,
                    "Служба ТС ПИоТ остановлена", "Служба ESM Orchestrator не запущена.", context.Esm.Details, DiagnosticFixKey.StartEsmServices,
                    Evidence("Service", orchestrator.ServiceName), Evidence("State", orchestrator.State)));
            else if (result.EsmRegistration == null || result.EsmRegistration.Kind == EsmRegistrationResultKind.Unavailable)
                Add(issues, Issue(DiagnosticIssueCode.ESM_REGISTRATION_STATUS_UNAVAILABLE, DiagnosticComponent.Esm, DiagnosticSeverity.WorkImpossible,
                    "Статус ТС ПИоТ недоступен", "Не удалось получить статус регистрации ТС ПИоТ.", context.Esm.Details,
                    DiagnosticFixKey.RestartEsm, Evidence("Probe", "instances/info")));
            else if (result.EsmRegistration.Kind == EsmRegistrationResultKind.NotConfigured)
                Add(issues, Issue(DiagnosticIssueCode.ESM_NOT_REGISTERED, DiagnosticComponent.Esm, DiagnosticSeverity.WorkImpossible,
                    "ТС ПИоТ не зарегистрирован", "Экземпляр ТС ПИоТ требует регистрации.", context.Esm.Details, DiagnosticFixKey.RegisterTsPiot));
            else if (hasEsmServiceEvidence && cm == null)
                Add(issues, Issue(DiagnosticIssueCode.ESM_SERVICE_MISSING, DiagnosticComponent.Esm, DiagnosticSeverity.WorkImpossible,
                    "Служба ТС ПИоТ не найдена", "Не найдена обязательная служба ТС ПИоТ.", context.Esm.Details, DiagnosticFixKey.RunSmartInstallation));
            else if (cm != null && !cm.IsRunning)
                Add(issues, Issue(DiagnosticIssueCode.ESM_CM_SERVICE_STOPPED, DiagnosticComponent.Esm, DiagnosticSeverity.WorkImpossible,
                    "Служба ТС ПИоТ остановлена", "Служба ESM CM не запущена.", context.Esm.Details, DiagnosticFixKey.StartEsmServices,
                    Evidence("Service", cm.ServiceName), Evidence("State", cm.State)));
            else if (result.EsmApiPort?.IsAvailable == false ||
                (result.EsmApiPort == null &&
                 (result.EsmApiStatus == null || result.EsmApiStatus.Kind == EsmStatusResultKind.Unavailable)))
                Add(issues, Issue(DiagnosticIssueCode.ESM_API_UNAVAILABLE, DiagnosticComponent.Esm, DiagnosticSeverity.WorkImpossible,
                    "ТС ПИоТ недоступен", "Локальный API ТС ПИоТ не отвечает.", context.Esm.Details, DiagnosticFixKey.RestartEsm,
                    Evidence("Probe", "EsmApiPort"), Evidence("Port", result.EsmApiPort?.Port?.ToString() ?? "not configured")));

            AddAtolIssues(issues, context);
            AddLmIssues(issues, context);

            if (!context.GisPathAvailable)
            {
                GisMtDiagnosticResult gis = result.GisMtDiagnostics;
                string title = gis?.State switch
                {
                    GisMtDiagnosticState.Unknown => "Состояние ГИС МТ не подтверждено",
                    GisMtDiagnosticState.Warning => gis.Summary,
                    _ => gis?.Summary ?? "ГИС МТ недоступна"
                };
                string message = gis?.State == GisMtDiagnosticState.Unknown
                    ? "Не удалось подтвердить состояние ГИС МТ."
                    : context.LmPathAvailable
                        ? $"{title}. Проверка выполняется через ЛМ ЧЗ."
                        : title + ".";
                DiagnosticEvidence[] gisEvidence = gis?.Evidence?
                    .Select((value, index) => Evidence("gisMt." + index, value))
                    .ToArray() ?? new[] { Evidence("gismt.code", context.Api?.Gismt?.Code?.ToString() ?? "missing") };
                Add(issues, Issue(DiagnosticIssueCode.GIS_MT_UNAVAILABLE, DiagnosticComponent.GisMt,
                    context.LmPathAvailable ? DiagnosticSeverity.Attention : DiagnosticSeverity.WorkImpossible,
                    title, message, context.Gismt.Details, evidence: gisEvidence));
            }

            ServiceSnapshot controllerService = FindService(result, "esm-lm-controller");
            if (!context.LmPathAvailable && controllerService == null)
                Add(issues, Issue(DiagnosticIssueCode.LM_CONTROLLER_DISCONNECTED, DiagnosticComponent.Controller,
                    context.GisPathAvailable ? DiagnosticSeverity.Attention : DiagnosticSeverity.WorkImpossible,
                    "Контроллер ЛМ ЧЗ не установлен", "Не найдена служба локального контроллера ЛМ ЧЗ.", context.Controller.Details,
                    DiagnosticFixKey.RunSmartInstallation));
            else if (!context.LmPathAvailable &&
                (context.Controller.State != DiagnosticState.Healthy ||
                 context.EsmController.State != DiagnosticConnectionState.Connected ||
                 context.LmConnection.State != DiagnosticConnectionState.Connected))
                Add(issues, Issue(DiagnosticIssueCode.LM_CONTROLLER_DISCONNECTED, DiagnosticComponent.Controller,
                    context.GisPathAvailable ? DiagnosticSeverity.Attention : DiagnosticSeverity.WorkImpossible,
                    "Контроллер не видит ЛМ ЧЗ", "Контроллер не видит ЛМ ЧЗ.",
                    context.EsmController.Details + Environment.NewLine + context.LmConnection.Details,
                    DiagnosticFixKey.RestartLmController));

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
                        $"Версия компонента «{version.ComponentName}» ниже минимально поддерживаемой.", VersionEvidence(version), DiagnosticFixKey.RunSmartInstallation));
                else if (version.State == ComponentVersionState.UpdateRequired)
                    Add(issues, Issue(DiagnosticIssueCode.COMPONENT_TARGET_VERSION_MISMATCH, DiagnosticComponent.ComponentVersion,
                        DiagnosticSeverity.Attention, "Доступно обновление компонента",
                        $"Для компонента «{version.ComponentName}» настроена другая версия.", VersionEvidence(version), DiagnosticFixKey.RunSmartInstallation));
            }

            return issues;
        }

        private static void AddAtolIssues(List<DiagnosticIssue> issues, DiagnosticEvaluationContext context)
        {
            KktDriverProbeResult driver = context.Result.KktDriver;
            bool driverMissing = driver != null
                ? !driver.DriverFound
                : string.IsNullOrWhiteSpace(context.Result.AtolDriverVersion) ||
                  context.Result.AtolDriverVersion.Contains("не установлен", StringComparison.OrdinalIgnoreCase);
            bool driverTooOld = driver != null
                ? driver.DriverFound && (!driver.HasKnownVersion || !driver.IsAtLeast(ComponentVersionRequirements.MinimumSupportedAtolDriver))
                : !driverMissing && !IsLegacyAtolSupported(context.Result.AtolDriverVersion);
            if (driver?.HasArchitectureMismatch == true)
                Add(issues, Issue(DiagnosticIssueCode.ATOL_DRIVER_ARCHITECTURE_MISMATCH, DiagnosticComponent.Kkt, DiagnosticSeverity.WorkImpossible,
                    "Разрядность драйвера ККТ не соответствует", "На ПК установлен драйвер АТОЛ другой разрядности, чем требуется для текущего клиента.", context.Kkt.Details,
                    DiagnosticFixKey.RunSmartInstallation, Evidence("ExpectedArchitecture", driver.ExpectedArchitecture)));
            else if (driverMissing)
                Add(issues, Issue(DiagnosticIssueCode.ATOL_DRIVER_MISSING, DiagnosticComponent.Kkt, DiagnosticSeverity.WorkImpossible,
                    "Драйвер ККТ не установлен", "Драйвер АТОЛ не найден.", context.Kkt.Details,
                    DiagnosticFixKey.RunSmartInstallation, Evidence("Architecture", driver?.ExpectedArchitecture ?? "unknown")));
            else if (driverTooOld)
                Add(issues, Issue(DiagnosticIssueCode.ATOL_DRIVER_TOO_OLD, DiagnosticComponent.Kkt, DiagnosticSeverity.WorkImpossible,
                    "Версия драйвера ККТ не поддерживается", "Обновите драйвер АТОЛ.", context.Kkt.Details,
                    DiagnosticFixKey.RunSmartInstallation, Evidence("Architecture", driver?.ExpectedArchitecture ?? "unknown"), Evidence("Version", driver?.InstalledVersion ?? context.Result.AtolDriverVersion ?? "unknown")));

            if (context.Result.KktPnP?.Kind == KktPnpResultKind.NotDetected)
                Add(issues, Issue(DiagnosticIssueCode.KKT_NOT_DETECTED, DiagnosticComponent.Kkt, DiagnosticSeverity.WorkImpossible,
                    "Windows не видит ККТ", "Windows не обнаружила подключённую кассу. Проверьте питание ККТ, USB/COM-подключение и повторите проверку.", context.Kkt.Details,
                    evidence: new[] { Evidence("PnP", "NotDetected") }));

            IReadOnlyList<ServiceSnapshot> services = context.Result.KktServiceStatus?.Services ?? AllServices(context.Result).ToArray();
            if (context.Result.KktServiceStatus != null)
            {
                string missingService = RequiredKktServices.FirstOrDefault(name => services.All(service =>
                    !string.Equals(service.ServiceName, name, StringComparison.OrdinalIgnoreCase)));
                if (missingService != null)
                    Add(issues, Issue(DiagnosticIssueCode.KKT_SERVICE_MISSING, DiagnosticComponent.Kkt, DiagnosticSeverity.WorkImpossible,
                        "Служба ККТ не найдена", "Не найдена обязательная служба ККТ.", context.Kkt.Details,
                        DiagnosticFixKey.RunSmartInstallation, Evidence("Service", missingService), Evidence("State", "missing")));
            }
            ServiceSnapshot stoppedService = services.FirstOrDefault(service =>
                RequiredKktServices.Contains(service.ServiceName, StringComparer.OrdinalIgnoreCase) && !service.IsRunning);
            if (stoppedService != null)
                Add(issues, Issue(DiagnosticIssueCode.ATOL_GRPC_SERVICE_STOPPED, DiagnosticComponent.Kkt, DiagnosticSeverity.WorkImpossible,
                    "Служба ККТ остановлена", "Служба ККТ не запущена.", context.Kkt.Details,
                    DiagnosticFixKey.StartKktServices,
                    Evidence("Service", stoppedService.ServiceName), Evidence("State", stoppedService.State)));

            if (context.EsmKkt.State == DiagnosticConnectionState.Disconnected &&
                context.Esm.State == DiagnosticState.Healthy &&
                context.Result.KktPnP?.Kind == KktPnpResultKind.Detected &&
                driver?.DriverFound == true &&
                RequiredKktServices.All(name => services.Any(service => string.Equals(service.ServiceName, name, StringComparison.OrdinalIgnoreCase) && service.IsRunning)) &&
                context.Result.EsmRegistration?.Kind == EsmRegistrationResultKind.Registered &&
                context.Result.EsmApiPort?.IsAvailable != false &&
                context.Result.EsmApiStatus?.Kind == EsmStatusResultKind.Success)
                Add(issues, Issue(DiagnosticIssueCode.KKT_NOT_VISIBLE_TO_ESM, DiagnosticComponent.Kkt, DiagnosticSeverity.WorkImpossible,
                    "ТС ПИоТ не видит ККТ", "Касса обнаружена Windows, драйвер и необходимые службы работают, но ТС ПИоТ её не видит. Перезапустите товароучётную систему и проверьте её подключение к ТС ПИоТ.", context.EsmKkt.Details));

            if (context.Kkt.State == DiagnosticState.Failed &&
                !issues.Any(issue => issue.Component == DiagnosticComponent.Kkt && issue.Severity == DiagnosticSeverity.WorkImpossible))
                Add(issues, Issue(DiagnosticIssueCode.ATOL_GRPC_SERVICE_STOPPED, DiagnosticComponent.Kkt, DiagnosticSeverity.WorkImpossible,
                    "Служба ККТ остановлена", "Служба ККТ не запущена.", context.Kkt.Details,
                    DiagnosticFixKey.StartKktServices));
        }

        private static void AddLmIssues(List<DiagnosticIssue> issues, DiagnosticEvaluationContext context)
        {
            AddLmInnIssues(issues, context.Result.LmProbe);

            LmDiagnosticProbeState state = context.Result.LmProbe?.State ??
                (context.Lm.State == DiagnosticState.Healthy ? LmDiagnosticProbeState.Available : LmDiagnosticProbeState.ApiUnavailable);
            if (state == LmDiagnosticProbeState.Available) return;
            DiagnosticSeverity severity = context.GisPathAvailable ? DiagnosticSeverity.Attention : DiagnosticSeverity.WorkImpossible;
            DiagnosticIssue issue = state switch
            {
                LmDiagnosticProbeState.NotInstalled => Issue(DiagnosticIssueCode.LM_NOT_INSTALLED, DiagnosticComponent.Lm, severity,
                    "ЛМ ЧЗ не установлен", "ЛМ ЧЗ не найден.", context.Lm.Details, DiagnosticFixKey.RunSmartInstallation),
                LmDiagnosticProbeState.SyncError => Issue(DiagnosticIssueCode.LM_SYNC_ERROR, DiagnosticComponent.Lm, severity,
                    "Ошибка синхронизации ЛМ ЧЗ", "ЛМ ЧЗ недоступен. Проверка выполняется через ГИС МТ.", context.Lm.Details, DiagnosticFixKey.RepairLmSync),
                LmDiagnosticProbeState.NotConfigured => Issue(DiagnosticIssueCode.LM_NOT_CONFIGURED, DiagnosticComponent.Lm, severity,
                    "ЛМ ЧЗ не настроен", "ЛМ ЧЗ требует настройки.", context.Lm.Details, DiagnosticFixKey.InitializeLm),
                LmDiagnosticProbeState.Initializing => Issue(DiagnosticIssueCode.LM_INITIALIZING, DiagnosticComponent.Lm, severity,
                    "ЛМ ЧЗ инициализируется", "Инициализация ЛМ ЧЗ ещё не завершена.", context.Lm.Details),
                LmDiagnosticProbeState.ApiUnavailable => Issue(DiagnosticIssueCode.LM_API_UNAVAILABLE, DiagnosticComponent.Lm, severity,
                    "ЛМ ЧЗ недоступен", context.GisPathAvailable
                        ? "ЛМ ЧЗ недоступен. Проверка выполняется через ГИС МТ."
                        : "API ЛМ ЧЗ не отвечает.", context.Lm.Details, DiagnosticFixKey.RestartLm),
                _ => Issue(DiagnosticIssueCode.LM_STATUS_UNKNOWN, DiagnosticComponent.Lm, DiagnosticSeverity.UnableToVerify,
                    "Статус ЛМ ЧЗ неизвестен", "ЛМ ЧЗ вернул неизвестный статус. Повторите проверку позже.", context.Lm.Details,
                    evidence: new[] { Evidence("lm.runtimeStatus", context.Result.LmProbe?.RuntimeStatus ?? "unknown") })
            };
            Add(issues, issue);
        }

        private static void AddLmInnIssues(List<DiagnosticIssue> issues, LmDiagnosticProbeResult probe)
        {
            if (probe?.State != LmDiagnosticProbeState.Available || !probe.HealthAvailable)
                return;

            DiagnosticEvidence[] evidence =
            {
                Evidence("actualLmInn", probe.ActualInnMasked ?? "-"),
                Evidence("expectedClientInn", probe.ExpectedInnMasked ?? "-"),
                Evidence("lm.runtimeStatus", probe.RuntimeStatus ?? "unknown"),
                Evidence("lm.apiAvailability", "Available")
            };
            if (probe.InnComparison == LmInnComparisonState.Mismatch)
                Add(issues, Issue(DiagnosticIssueCode.LM_INN_MISMATCH, DiagnosticComponent.Lm, DiagnosticSeverity.Attention,
                    "ИНН ЛМ ЧЗ не соответствует клиенту", "ЛМ ЧЗ настроен на другой ИНН.",
                    probe.InnComparisonDetails, DiagnosticFixKey.ConfirmLmClientMismatch, evidence: evidence));
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
        private static readonly string[] RequiredKktServices = { "uem-agent", "uem-updater", "atol-grpc-service" };
        private static bool IsLegacyAtolSupported(string value)
        {
            string token = value?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return Version.TryParse(token, out Version installed) &&
                Version.TryParse(ComponentVersionRequirements.MinimumSupportedAtolDriver, out Version minimum) &&
                installed >= minimum;
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
                    version.State is ComponentVersionState.Current or ComponentVersionState.Installed ? DiagnosticFactState.Success :
                    version.State is ComponentVersionState.Unknown ? DiagnosticFactState.Unknown : DiagnosticFactState.Failure,
                    version.InstalledVersion ?? "not installed", VersionEvidence(version)));

            ComponentVersionStatus controllerVersion = (context.Versions ?? Array.Empty<ComponentVersionStatus>())
                .FirstOrDefault(version => string.Equals(version.ComponentName, "Контроллер", StringComparison.OrdinalIgnoreCase));
            ServiceSnapshot controllerService = context.Result.ControllerServiceStatus?.Services?.FirstOrDefault() ??
                (context.Result.ServiceSnapshots ?? Array.Empty<ServiceSnapshot>()).FirstOrDefault(service =>
                    string.Equals(service.ServiceName, "esm-lm-controller", StringComparison.OrdinalIgnoreCase));
            ControllerServiceInfoResult serviceInfo = context.Result.ControllerServiceInfo;
            facts.Add(new DiagnosticFact("Controller.Installed", DiagnosticComponent.Controller,
                controllerVersion?.State == ComponentVersionState.NotInstalled ? DiagnosticFactState.Failure :
                controllerVersion is null ? DiagnosticFactState.Unknown : DiagnosticFactState.Success,
                controllerVersion?.State == ComponentVersionState.NotInstalled ? "NotInstalled" : "Installed"));
            facts.Add(new DiagnosticFact("Controller.InstalledVersion", DiagnosticComponent.Controller,
                string.IsNullOrWhiteSpace(controllerVersion?.InstalledVersion) ? DiagnosticFactState.Unknown : DiagnosticFactState.Success,
                controllerVersion?.InstalledVersion ?? "-"));
            facts.Add(new DiagnosticFact("Controller.TargetVersion", DiagnosticComponent.Controller,
                string.IsNullOrWhiteSpace(controllerVersion?.TargetVersion) ? DiagnosticFactState.Unknown : DiagnosticFactState.Success,
                controllerVersion?.TargetVersion ?? "-"));
            facts.Add(new DiagnosticFact("Controller.VersionMatch", DiagnosticComponent.Controller,
                controllerVersion?.State is ComponentVersionState.Current or ComponentVersionState.Installed ? DiagnosticFactState.Success :
                controllerVersion?.State is ComponentVersionState.UpdateRequired or ComponentVersionState.BelowMinimum ? DiagnosticFactState.Failure : DiagnosticFactState.Unknown,
                controllerVersion?.State.ToString() ?? "Unknown"));
            facts.Add(new DiagnosticFact("Controller.ServiceExists", DiagnosticComponent.Controller,
                controllerService is null ? DiagnosticFactState.Failure : DiagnosticFactState.Success,
                controllerService?.ServiceName ?? "missing"));
            facts.Add(new DiagnosticFact("Controller.ServiceRunning", DiagnosticComponent.Controller,
                controllerService?.IsRunning == true ? DiagnosticFactState.Success :
                controllerService is null ? DiagnosticFactState.Unknown : DiagnosticFactState.Failure,
                controllerService?.State ?? "missing"));
            facts.Add(new DiagnosticFact("Controller.ServiceInfoAvailable", DiagnosticComponent.Controller,
                serviceInfo?.IsAvailable == true ? DiagnosticFactState.Success :
                serviceInfo is null ? DiagnosticFactState.Unknown : DiagnosticFactState.Failure,
                serviceInfo?.IsAvailable == true ? "Available" : "Unavailable",
                $"httpStatus={serviceInfo?.HttpStatusCode?.ToString() ?? "-"}; errorCategory={serviceInfo?.ErrorCategory ?? "-"}"));
            facts.Add(new DiagnosticFact("Controller.ServiceInfoHttpStatus", DiagnosticComponent.Controller,
                serviceInfo?.HttpStatusCode.HasValue == true ? DiagnosticFactState.Success : DiagnosticFactState.Unknown,
                serviceInfo?.HttpStatusCode?.ToString() ?? "-"));
            facts.Add(new DiagnosticFact("Controller.ServiceInfoErrorCategory", DiagnosticComponent.Controller,
                string.IsNullOrWhiteSpace(serviceInfo?.ErrorCategory) ? DiagnosticFactState.Success : DiagnosticFactState.Failure,
                string.IsNullOrWhiteSpace(serviceInfo?.ErrorCategory) ? "-" : serviceInfo.ErrorCategory));

            EsmComponentStatus client = context.Api?.ClientSoftware;
            facts.Add(new DiagnosticFact("Esm.Api", DiagnosticComponent.Esm,
                context.Result.EsmApiPort?.IsAvailable == true ? DiagnosticFactState.Success :
                context.Result.EsmApiPort == null ? DiagnosticFactState.Unknown : DiagnosticFactState.Failure,
                context.Result.EsmApiPort?.IsAvailable == true ? "Available" : "Unavailable", context.Esm.Details));
            facts.Add(new DiagnosticFact("EsmApiPort", DiagnosticComponent.Esm,
                context.Result.EsmApiPort?.IsAvailable == true ? DiagnosticFactState.Success :
                context.Result.EsmApiPort == null ? DiagnosticFactState.Unknown : DiagnosticFactState.Failure,
                context.Result.EsmApiPort?.Port?.ToString() ?? "-",
                $"errorCategory={context.Result.EsmApiPort?.ErrorCategory ?? "-"}"));
            facts.Add(new DiagnosticFact("Esm.Registration", DiagnosticComponent.Esm,
                context.Result.EsmRegistration?.Kind == EsmRegistrationResultKind.Registered ? DiagnosticFactState.Success :
                context.Result.EsmRegistration == null ? DiagnosticFactState.Unknown : DiagnosticFactState.Failure,
                context.Result.EsmRegistration?.Kind.ToString() ?? "unknown"));
            facts.Add(new DiagnosticFact("Kkt.Live", DiagnosticComponent.Kkt,
                context.EsmKkt.State == DiagnosticConnectionState.Connected ? DiagnosticFactState.Success :
                context.EsmKkt.State == DiagnosticConnectionState.Disconnected ? DiagnosticFactState.Failure : DiagnosticFactState.Unknown,
                context.Result.CashRegister?.Kind.ToString() ?? "unknown", context.EsmKkt.Details));
            facts.Add(new DiagnosticFact("Kkt.PnP", DiagnosticComponent.Kkt,
                context.Result.KktPnP?.Kind == KktPnpResultKind.Detected ? DiagnosticFactState.Success :
                context.Result.KktPnP?.Kind == KktPnpResultKind.NotDetected ? DiagnosticFactState.Failure : DiagnosticFactState.Unknown,
                context.Result.KktPnP?.Kind.ToString() ?? "unknown", context.Result.KktPnP?.Details));
            KktDriverProbeResult kktDriver = context.Result.KktDriver;
            facts.Add(new DiagnosticFact("Kkt.DriverArchitecture", DiagnosticComponent.Kkt,
                kktDriver == null ? DiagnosticFactState.Unknown : DiagnosticFactState.Success,
                kktDriver?.RequiredArchitecture ?? "unknown"));
            facts.Add(new DiagnosticFact("Kkt.Driver", DiagnosticComponent.Kkt,
                kktDriver?.DriverFound == true ? DiagnosticFactState.Success :
                kktDriver == null ? DiagnosticFactState.Unknown : DiagnosticFactState.Failure,
                kktDriver?.InstalledVersion ?? "not installed",
                $"minimum={ComponentVersionRequirements.MinimumSupportedAtolDriver}; error={kktDriver?.ErrorCategory ?? "-"}"));
            KktPortProbeResult kktPort = context.Result.KktPort4041;
            facts.Add(new DiagnosticFact("Kkt.Port4041", DiagnosticComponent.Kkt,
                kktPort?.IsAvailable == true ? DiagnosticFactState.Success :
                kktPort == null ? DiagnosticFactState.Unknown : DiagnosticFactState.Failure,
                kktPort?.IsAvailable == true ? "Success" : "Failure",
                $"endpoint=127.0.0.1:4041; result={kktPort?.ErrorCategory ?? (kktPort?.IsAvailable == true ? "success" : "unknown")}"));
            facts.Add(new DiagnosticFact("ClientSoftware", DiagnosticComponent.ClientSoftware,
                DiagnosticFactState.Unknown, client?.Name ?? "not detected", ClientSoftwareEvidence(client)));
            facts.Add(new DiagnosticFact("Lm.Health", DiagnosticComponent.Lm,
                context.Lm.State == DiagnosticState.Healthy ? DiagnosticFactState.Success :
                context.Lm.State == DiagnosticState.Failed ? DiagnosticFactState.Failure : DiagnosticFactState.Unknown,
                context.Result.LmProbe?.State.ToString() ?? context.Lm.State.ToString(), context.Lm.Details));
            LmDiagnosticProbeResult lmProbe = context.Result.LmProbe;
            facts.Add(new DiagnosticFact("LmInnComparison", DiagnosticComponent.Lm,
                lmProbe?.InnComparison == LmInnComparisonState.Match ? DiagnosticFactState.Success :
                lmProbe?.InnComparison is LmInnComparisonState.Mismatch or LmInnComparisonState.Missing ? DiagnosticFactState.Failure : DiagnosticFactState.Unknown,
                lmProbe?.InnComparison.ToString() ?? "Unknown",
                $"Reason={lmProbe?.InnComparisonDetails ?? "LM probe unavailable"}; " +
                $"actual={lmProbe?.ActualInnMasked ?? "-"}; expected={lmProbe?.ExpectedInnMasked ?? "-"}; " +
                $"runtime={lmProbe?.RuntimeStatus ?? "unknown"}; api={lmProbe?.State.ToString() ?? "unknown"}"));
            facts.Add(ConnectionFact("Lm.Controller", DiagnosticComponent.Controller, context.EsmController));
            facts.Add(new DiagnosticFact("EsmToController.LmDataCode", DiagnosticComponent.Controller,
                context.Result.EsmApiStatus?.Status?.LmInfo?.EffectiveCode == 0 ? DiagnosticFactState.Success :
                context.Result.EsmApiStatus?.Status?.LmInfo?.EffectiveCode.HasValue == true ? DiagnosticFactState.Failure : DiagnosticFactState.Unknown,
                context.Result.EsmApiStatus?.Status?.LmInfo?.EffectiveCode?.ToString() ?? "missing"));
            facts.Add(ConnectionFact("EsmToController.LinkState", DiagnosticComponent.Controller, context.EsmController));
            facts.Add(ConnectionFact("Lm.Connection", DiagnosticComponent.Lm, context.LmConnection));
            AddGisMtFacts(facts, context);
            return facts;
        }

        private static void AddGisMtFacts(ICollection<DiagnosticFact> facts, DiagnosticEvaluationContext context)
        {
            GisMtDiagnosticResult gis = context.Result.GisMtDiagnostics;
            facts.Add(new DiagnosticFact("GisMt", DiagnosticComponent.GisMt,
                context.Gismt.State == DiagnosticState.Healthy ? DiagnosticFactState.Success :
                context.Gismt.State == DiagnosticState.Failed ? DiagnosticFactState.Failure : DiagnosticFactState.Unknown,
                gis?.State.ToString() ?? context.Api?.Gismt?.Code?.ToString() ?? "unknown",
                context.Gismt.Details));
            if (gis == null) return;

            facts.Add(GisMtFact("GisMt.ControlledChannel", gis.ControlledChannelState,
                gis.ControlledChannelState.ToString(),
                $"observedUtc={gis.LastControlledChannelSuccessUtc?.ToString("O") ?? gis.LastControlledChannelErrorUtc?.ToString("O") ?? "-"}; details={gis.ControlledChannelDetails ?? gis.LastControlledChannelError ?? "-"}"));
            facts.Add(GisMtFact("GisMt.CdnTransport", gis.CdnTransportState,
                gis.CdnTransportState.ToString(),
                $"lastSuccessUtc={gis.LastCdnTransportSuccessUtc?.ToString("O") ?? "-"}; lastErrorUtc={gis.LastCdnTransportErrorUtc?.ToString("O") ?? "-"}; error={gis.LastCdnTransportError ?? "-"}"));
            facts.Add(GisMtFact("GisMt.ApplicationExchange", gis.ApplicationExchangeState,
                gis.ApplicationExchangeState.ToString(),
                $"lastSuccessUtc={gis.LastSuccessfulGisExchangeUtc?.ToString("O") ?? "-"}; lastErrorUtc={gis.LastApplicationErrorUtc?.ToString("O") ?? "-"}; error={gis.LastApplicationError ?? "-"}"));
            facts.Add(new DiagnosticFact("GisMt.CdnConfiguration", DiagnosticComponent.GisMt,
                gis.ConfiguredCdnCount > 0 ? DiagnosticFactState.Success : DiagnosticFactState.Failure,
                gis.ConfiguredCdnCount.ToString(), string.Join(", ", gis.ConfiguredCdns ?? Array.Empty<string>())));
            facts.Add(new DiagnosticFact("GisMt.CdnCache", DiagnosticComponent.GisMt,
                gis.CachedCdnCount == 0 ? DiagnosticFactState.Unknown :
                gis.AvailableCdnCount > 0 ? DiagnosticFactState.Success : DiagnosticFactState.Failure,
                $"available={gis.AvailableCdnCount}; blocked={gis.BlockedCdnCount}; total={gis.CachedCdnCount}",
                $"checkedUtc={gis.CacheLastCheckedUtc?.ToString("O") ?? "-"}; latencyMs={gis.LastLatencyMs?.ToString() ?? "-"}"));
        }

        private static DiagnosticFact GisMtFact(string key, GisMtEvidenceState state, string value, string evidence) =>
            new(key, DiagnosticComponent.GisMt,
                state == GisMtEvidenceState.Healthy ? DiagnosticFactState.Success :
                state == GisMtEvidenceState.Error ? DiagnosticFactState.Failure : DiagnosticFactState.Unknown,
                value, evidence);

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
