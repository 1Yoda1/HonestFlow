using HonestFlow.Application.Licensing;

namespace HonestFlow.Application.Bootstrap
{
    public enum StartupPresentationPhase
    {
        Initializing,
        PreparationFailed,
        FreshLogin,
        RememberedAccessChecking,
        ManualAccessChecking,
        ClientAccessDisabled,
        DeviceAwaitingAddress,
        DevicePending,
        DeviceStatusUnavailable,
        DeviceRejected,
        DeviceDenied,
        LicenseChecking,
        LicenseNotIssued,
        LicenseDenied,
        AllowedOnline,
        AllowedOffline,
        Launched
    }

    public enum StartupStageVisualState
    {
        Inactive,
        Active,
        Complete,
        Error
    }

    public sealed class StartupStagePresentation
    {
        public StartupStageVisualState Preparation { get; init; }
        public StartupStageVisualState Access { get; init; }
        public StartupStageVisualState Device { get; init; }
        public StartupStageVisualState License { get; init; }
        public StartupStageVisualState Launch { get; init; }
        public string StatusText { get; init; }
    }

    public static class StartupStagePresentationMapper
    {
        public static StartupPresentationPhase PhaseForRegistration(DeviceRegistrationStartupState state) => state switch
        {
            DeviceRegistrationStartupState.Pending => StartupPresentationPhase.DevicePending,
            DeviceRegistrationStartupState.Rejected => StartupPresentationPhase.DeviceRejected,
            DeviceRegistrationStartupState.StatusUnavailable => StartupPresentationPhase.DeviceStatusUnavailable,
            DeviceRegistrationStartupState.ApprovedNotReady => StartupPresentationPhase.LicenseChecking,
            DeviceRegistrationStartupState.SessionInvalid => StartupPresentationPhase.FreshLogin,
            DeviceRegistrationStartupState.Allowed => StartupPresentationPhase.AllowedOnline,
            _ => StartupPresentationPhase.DeviceAwaitingAddress
        };

        public static StartupPresentationPhase PhaseForLicense(LicenseObservationSnapshot snapshot)
        {
            if (snapshot?.TechnicalCode == "CLIENT_ACCESS_DISABLED")
                return StartupPresentationPhase.ClientAccessDisabled;
            if (snapshot?.Decision == LicenseDecision.DeviceDisabled)
                return StartupPresentationPhase.DeviceDenied;
            if (snapshot?.Decision == LicenseDecision.LicenseNotIssued)
                return StartupPresentationPhase.LicenseNotIssued;
            if (snapshot?.Decision == LicenseDecision.Allowed)
            {
                return snapshot.ManifestSource == LicenseManifestSource.Cache
                    ? StartupPresentationPhase.AllowedOffline
                    : StartupPresentationPhase.AllowedOnline;
            }
            return StartupPresentationPhase.LicenseDenied;
        }

        public static StartupStagePresentation Create(StartupPresentationPhase phase) => phase switch
        {
            StartupPresentationPhase.Initializing => Presentation(
                StartupStageVisualState.Active,
                statusText: "Подготавливаем HonestFlow…"),
            StartupPresentationPhase.PreparationFailed => Presentation(
                StartupStageVisualState.Error,
                statusText: "Не удалось подготовить HonestFlow."),
            StartupPresentationPhase.FreshLogin => Presentation(
                StartupStageVisualState.Complete,
                StartupStageVisualState.Active,
                statusText: "Введите код клиента."),
            StartupPresentationPhase.RememberedAccessChecking => Presentation(
                StartupStageVisualState.Complete,
                StartupStageVisualState.Active,
                statusText: "Проверяем сохранённый доступ…"),
            StartupPresentationPhase.ManualAccessChecking => Presentation(
                StartupStageVisualState.Complete,
                StartupStageVisualState.Active,
                statusText: "Проверяем код клиента…"),
            StartupPresentationPhase.ClientAccessDisabled => Presentation(
                StartupStageVisualState.Complete,
                StartupStageVisualState.Error,
                statusText: "Доступ клиента к HonestFlow отключён."),
            StartupPresentationPhase.DeviceAwaitingAddress => Presentation(
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                StartupStageVisualState.Active,
                statusText: "Проверяем регистрацию устройства…"),
            StartupPresentationPhase.DevicePending => Presentation(
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                StartupStageVisualState.Active,
                statusText: "Ожидаем подтверждения регистрации…"),
            StartupPresentationPhase.DeviceStatusUnavailable => Presentation(
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                StartupStageVisualState.Active,
                statusText: "Не удалось проверить статус регистрации. Соединение с сервером недоступно."),
            StartupPresentationPhase.DeviceRejected => Presentation(
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                StartupStageVisualState.Error,
                statusText: "Регистрация устройства отклонена."),
            StartupPresentationPhase.DeviceDenied => Presentation(
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                StartupStageVisualState.Error,
                statusText: "Доступ устройства отключён."),
            StartupPresentationPhase.LicenseChecking => Presentation(
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                StartupStageVisualState.Active,
                statusText: "Проверяем лицензию…"),
            StartupPresentationPhase.LicenseNotIssued => Presentation(
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                StartupStageVisualState.Active,
                statusText: "Ожидаем выдачи лицензии…"),
            StartupPresentationPhase.LicenseDenied => Presentation(
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                StartupStageVisualState.Error,
                statusText: "Лицензия не разрешает запуск HonestFlow."),
            StartupPresentationPhase.AllowedOnline => Presentation(
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                StartupStageVisualState.Active,
                "Запускаем HonestFlow…"),
            StartupPresentationPhase.AllowedOffline => Presentation(
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                StartupStageVisualState.Active,
                "Запускаем HonestFlow в автономном режиме…"),
            StartupPresentationPhase.Launched => Presentation(
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                StartupStageVisualState.Complete,
                "HonestFlow запущен."),
            _ => Presentation(StartupStageVisualState.Active, statusText: "Подготавливаем HonestFlow…")
        };

        private static StartupStagePresentation Presentation(
            StartupStageVisualState preparation,
            StartupStageVisualState access = StartupStageVisualState.Inactive,
            StartupStageVisualState device = StartupStageVisualState.Inactive,
            StartupStageVisualState license = StartupStageVisualState.Inactive,
            StartupStageVisualState launch = StartupStageVisualState.Inactive,
            string statusText = null) =>
            new()
            {
                Preparation = preparation,
                Access = access,
                Device = device,
                License = license,
                Launch = launch,
                StatusText = statusText
            };
    }
}
