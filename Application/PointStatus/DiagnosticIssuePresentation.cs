using System;
using System.Linq;

namespace HonestFlow.Application.PointStatus;

/// <summary>
/// User-facing wording for diagnostics. Raw probe output remains in DiagnosticsSnapshot.
/// </summary>
public sealed class DiagnosticIssuePresentation
{
    public DiagnosticIssuePresentation(string title, string description, string recommendation, string technicalDetails)
    {
        Title = title;
        Description = description;
        Recommendation = recommendation;
        TechnicalDetails = technicalDetails;
    }

    public string Title { get; }
    public string Description { get; }
    public string Recommendation { get; }
    public string TechnicalDetails { get; }
}

public sealed class DiagnosticIssuePresentationMapper
{
    public DiagnosticIssuePresentation Create(DiagnosticsSnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

        string technicalDetails = string.Join(Environment.NewLine, snapshot.Reasons);
        DiagnosticIssue primaryIssue = snapshot.Issues
            .OrderByDescending(issue => issue.Severity)
            .FirstOrDefault();
        if (primaryIssue != null)
        {
            return Issue(
                primaryIssue.Title,
                primaryIssue.UserMessage,
                Recommendation(primaryIssue.SuggestedFix),
                primaryIssue.TechnicalDetails);
        }

        string userDescription = snapshot.UserMessages.FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));

        if (snapshot.Esm.State == DiagnosticState.Failed)
            return Issue(
                "ТС ПИоТ недоступен",
                "Локальный API ТС ПИоТ не отвечает или компонент не готов к работе.",
                "Проверьте службы ESM или выполните автоматическое восстановление.",
                technicalDetails);

        if (snapshot.Kkt.State == DiagnosticState.Failed || snapshot.EsmToKkt.State == DiagnosticConnectionState.Disconnected)
            return Issue(
                "ККТ не подключена",
                snapshot.UserMessages.FirstOrDefault(message => message.StartsWith("ККТ", StringComparison.Ordinal)) ??
                    "HonestFlow не может подтвердить связь с кассой.",
                "Проверьте подключение ККТ и службу кассового драйвера.",
                technicalDetails);

        if (snapshot.Lm.State == DiagnosticState.Failed && snapshot.Gismt.State == DiagnosticState.Failed)
            return Issue(
                "Системы маркировки недоступны",
                "Не удалось получить состояние ЛМ ЧЗ и ГИС МТ.",
                "Проверьте сетевое подключение и повторите проверку позже.",
                technicalDetails);

        if (snapshot.WorkState == WorkState.Attention && userDescription != null)
            return Issue(
                "Требуется внимание",
                userDescription,
                "Откройте схему и повторите проверку после устранения проблемы.",
                technicalDetails);

        if (snapshot.Controller.State == DiagnosticState.Failed)
            return Issue(
                "Локальный контроллер недоступен",
                "HonestFlow не может подтвердить состояние локального контроллера.",
                "Проверьте, что служба контроллера запущена, и повторите проверку.",
                technicalDetails);

        if (snapshot.WorkState == WorkState.Ready)
            return Issue("Проблемы не обнаружены", "Рабочее место готово к работе.", string.Empty, technicalDetails);

        return Issue(
            "Требуется проверка",
            userDescription ?? "Состояние одного из компонентов требует внимания.",
            "Откройте схему и повторите проверку после устранения проблемы.",
            technicalDetails);
    }

    public string SimpleDescription(WorkState workState) => workState switch
    {
        WorkState.WorkImpossible => "Один из необходимых компонентов не работает или отсутствует.\nHonestFlow может попробовать восстановить его автоматически.",
        WorkState.UnableToVerify => "Не удалось подтвердить готовность рабочего места. Повторите проверку позже.",
        WorkState.Attention => "Один из компонентов требует внимания. Откройте подробную схему для проверки.",
        _ => "Касса и маркировка работают нормально. Можно продолжать работу."
    };

    public string ComponentStatus(DiagnosticComponentFact fact, string failedStatus) => fact.State switch
    {
        DiagnosticState.Healthy => "Доступно",
        DiagnosticState.Failed => failedStatus,
        _ => "Нет данных"
    };

    public string GisMtStatus(DiagnosticComponentFact fact) =>
        !string.IsNullOrWhiteSpace(fact?.Summary)
            ? fact.Summary
            : ComponentStatus(fact, "Недоступна");

    public string ConnectionStatus(DiagnosticConnectionFact fact) => fact.State switch
    {
        DiagnosticConnectionState.Connected => "Подключено",
        DiagnosticConnectionState.Disconnected => "Не подключена",
        _ => "Не проверена"
    };

    public string ComponentDetails(DiagnosticComponentFact fact, string componentName)
    {
        if (!string.IsNullOrWhiteSpace(fact?.Summary))
            return string.IsNullOrWhiteSpace(fact.Details)
                ? fact.Summary
                : fact.Summary + Environment.NewLine + fact.Details;
        return fact?.State switch
        {
            DiagnosticState.Healthy => $"{componentName}: доступно.",
            DiagnosticState.Failed => $"{componentName}: требуется проверка.",
            _ => $"{componentName}: состояние пока не получено."
        };
    }

    public string ConnectionDetails(DiagnosticConnectionFact fact, string connectionName) => fact.State switch
    {
        DiagnosticConnectionState.Connected => $"{connectionName}: подключено.",
        DiagnosticConnectionState.Disconnected => $"{connectionName}: не подключено.",
        _ => $"{connectionName}: состояние пока не получено."
    };

    private static DiagnosticIssuePresentation Issue(string title, string description, string recommendation, string technicalDetails) =>
        new(title, description, recommendation, technicalDetails);

    private static string Recommendation(DiagnosticFixKey? fix) => fix switch
    {
        DiagnosticFixKey.RestartEsm => "Перезапустите ТС ПИоТ или выполните автоматическое восстановление.",
        DiagnosticFixKey.StartEsmServices => "Запустите службы ТС ПИоТ или выполните автоматическое восстановление.",
        DiagnosticFixKey.StartKktServices => "Запустите службы ККТ или выполните автоматическое восстановление.",
        DiagnosticFixKey.RestartLm => "Перезапустите ЛМ ЧЗ или выполните автоматическое восстановление.",
        DiagnosticFixKey.RepairLmSync => "Проверьте синхронизацию ЛМ ЧЗ.",
        DiagnosticFixKey.InitializeLm => "Выполните настройку ЛМ ЧЗ.",
        DiagnosticFixKey.RunSmartInstallation => "Выполните умную установку компонентов.",
        DiagnosticFixKey.RegisterTsPiot => "Зарегистрируйте ТС ПИоТ.",
        DiagnosticFixKey.ConfirmLmClientMismatch => "Подтвердите организацию перед переустановкой ЛМ ЧЗ.",
        DiagnosticFixKey.RestartLmController => "Перезапустите службу локального контроллера ЛМ ЧЗ.",
        _ => string.Empty
    };
}
