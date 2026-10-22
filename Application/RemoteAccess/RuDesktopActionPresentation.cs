namespace HonestFlow.Application.RemoteAccess;

public enum RuDesktopActionKind
{
    None,
    Refresh,
    Install,
    Reinstall,
    StartService,
    SetupPassword,
    RequestHelp
}

public sealed record RuDesktopActionPresentation(RuDesktopActionKind Action, string Text, string Hint)
{
    public bool IsEnabled => Action != RuDesktopActionKind.None;

    public static RuDesktopActionPresentation Create(
        RuDesktopStatus? status,
        bool enabledForClient,
        bool rebootRequired = false)
    {
        if (!enabledForClient)
            return new(RuDesktopActionKind.None, "RuDesktop: отключён для клиента",
                "Включите RuDesktop в карточке клиента.");
        if (rebootRequired)
            return new(RuDesktopActionKind.None, "RuDesktop: перезагрузите ПК",
                "Установка завершена; Windows требуется перезагрузка.");
        if (status is null)
            return new(RuDesktopActionKind.Refresh, "Проверить RuDesktop", "Проверить состояние на этом компьютере.");
        if (status.InstallationState is RuDesktopInstallationState.CheckFailed or RuDesktopInstallationState.Unknown ||
            !string.IsNullOrWhiteSpace(status.ErrorMessage))
            return new(RuDesktopActionKind.Refresh, "Повторить проверку RuDesktop",
                status.ErrorMessage ?? "Состояние RuDesktop не определено.");

        return status.InstallationState switch
        {
            RuDesktopInstallationState.NotInstalled =>
                new(RuDesktopActionKind.Install, "Установить RuDesktop",
                    "Скачать и установить проверенный пакет RuDesktop."),
            RuDesktopInstallationState.Damaged =>
                new(RuDesktopActionKind.Reinstall, "Переустановить RuDesktop",
                    "Программа или служба RuDesktop отсутствует."),
            RuDesktopInstallationState.ServiceStopped =>
                new(RuDesktopActionKind.StartService, "Запустить службу RuDesktop",
                    "Программа установлена, но служба остановлена."),
            RuDesktopInstallationState.Ready when string.IsNullOrWhiteSpace(status.Id) =>
                new(RuDesktopActionKind.Refresh, "Получить ID RuDesktop",
                    "Служба запущена, но ID пока не получен."),
            RuDesktopInstallationState.Ready when status.RevocationPending =>
                new(RuDesktopActionKind.None, "Отзыв доступа не завершён",
                    "Повторите отключение доступа или синхронизацию с сервером."),
            RuDesktopInstallationState.Ready when !status.RemoteCredentialSynced =>
                new(RuDesktopActionKind.SetupPassword, "Настроить доступ",
                    $"Служба работает, ID: {status.Id}. Создать отдельный пароль этого ПК для HonestDesk."),
            RuDesktopInstallationState.Ready =>
                new(RuDesktopActionKind.RequestHelp, "Запросить помощь",
                    $"ID: {status.Id}. Доступ HonestDesk готов."),
            _ => new(RuDesktopActionKind.Refresh, "Повторить проверку RuDesktop",
                "Состояние RuDesktop не определено.")
        };
    }
}
