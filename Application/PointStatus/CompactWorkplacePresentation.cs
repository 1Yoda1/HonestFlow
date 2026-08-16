using System;

namespace HonestFlow.Application.PointStatus;

public enum CompactWorkplaceVisualState
{
    Neutral,
    Ready,
    Attention,
    UnableToVerify,
    WorkImpossible
}

public sealed class CompactWorkplacePresentation
{
    private CompactWorkplacePresentation(
        string title,
        string description,
        string cloudStatus,
        DateTimeOffset? checkedAtUtc,
        bool canAutoFix,
        CompactWorkplaceVisualState visualState,
        string accentColor,
        string tintColor,
        string borderColor)
    {
        Title = title;
        Description = description;
        CloudStatus = cloudStatus;
        CheckedAtUtc = checkedAtUtc;
        CanAutoFix = canAutoFix;
        VisualState = visualState;
        AccentColor = accentColor;
        TintColor = tintColor;
        BorderColor = borderColor;
    }

    public string Title { get; }
    public string Description { get; }
    public string CloudStatus { get; }
    public DateTimeOffset? CheckedAtUtc { get; }
    public bool CanAutoFix { get; }
    public CompactWorkplaceVisualState VisualState { get; }
    public string AccentColor { get; }
    public string TintColor { get; }
    public string BorderColor { get; }

    public static CompactWorkplacePresentation Create(
        DiagnosticsSnapshot snapshot,
        HonestFlowCloudStatus cloudStatus)
    {
        if (snapshot is null)
            return new CompactWorkplacePresentation(
                "Проверяем рабочее место…",
                "Пожалуйста, подождите.",
                BuildCloudStatus(cloudStatus),
                null,
                false,
                CompactWorkplaceVisualState.Neutral,
                "#64748B",
                "#FFFFFF",
                "#DDE3EC");

        (string title, string description, bool canAutoFix, CompactWorkplaceVisualState visualState, string accentColor, string tintColor, string borderColor) = snapshot.WorkState switch
        {
            WorkState.WorkImpossible => (
                "Работа невозможна",
                "Обнаружена критическая проблема.\nHonestFlow может попробовать исправить её автоматически.",
                true,
                CompactWorkplaceVisualState.WorkImpossible,
                "#C6283D",
                "#FFF1F3",
                "#F4BAC3"),
            WorkState.UnableToVerify => (
                "Работа не подтверждена",
                "Не удалось подтвердить готовность рабочего места. Повторите проверку позже.",
                false,
                CompactWorkplaceVisualState.UnableToVerify,
                "#C66A12",
                "#FFF5EA",
                "#F2CEA8"),
            WorkState.Attention => (
                "Работа возможна",
                "Обнаружены проблемы, не мешающие работе.",
                true,
                CompactWorkplaceVisualState.Attention,
                "#B7791F",
                "#FFF8E1",
                "#F1D794"),
            _ => (
                "Работа возможна",
                "Касса и маркировка готовы к работе.",
                false,
                CompactWorkplaceVisualState.Ready,
                "#137A4B",
                "#EDF9F1",
                "#B9E3C9")
        };

        return new CompactWorkplacePresentation(
            title,
            description,
            BuildCloudStatus(cloudStatus),
            snapshot.ObservedAtUtc,
            canAutoFix,
            visualState,
            accentColor,
            tintColor,
            borderColor);
    }

    private static string BuildCloudStatus(HonestFlowCloudStatus cloudStatus) => cloudStatus switch
    {
        HonestFlowCloudStatus.Available => "Облако доступно",
        HonestFlowCloudStatus.Unavailable => "Облако недоступно",
        _ => "Облако: состояние неизвестно"
    };
}
