using System;
using System.Linq;
using HonestFlow.Application.Licensing;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.ServiceConnection;

public static class ServiceEntitlementEvaluator
{
    public static ServiceConnectionState Evaluate(LicenseObservationSnapshot? snapshot)
    {
        if (snapshot is null)
            return ServiceConnectionState.Unavailable;

        return snapshot.Decision switch
        {
            LicenseDecision.Allowed when snapshot.Features?.Contains(LicenseFeature.Service) == true &&
                                         !string.IsNullOrWhiteSpace(snapshot.ClientId) &&
                                         !string.IsNullOrWhiteSpace(snapshot.DeviceId) => ServiceConnectionState.Active,
            LicenseDecision.Allowed => ServiceConnectionState.NotEntitled,
            LicenseDecision.ClientDisabled or LicenseDecision.DeviceDisabled => ServiceConnectionState.Disabled,
            LicenseDecision.DeviceNotRegistered => ServiceConnectionState.RegistrationRequired,
            LicenseDecision.LicenseNotIssued => ServiceConnectionState.NotEntitled,
            LicenseDecision.ManifestExpired or LicenseDecision.OfflineGraceExpired => ServiceConnectionState.Expired,
            LicenseDecision.VersionTooOld => ServiceConnectionState.UpdateRequired,
            _ => ServiceConnectionState.Unavailable
        };
    }

    public static string Message(ServiceConnectionState state, string? fallback = null) => state switch
    {
        ServiceConnectionState.Active => "HonestFlow Service подключён.",
        ServiceConnectionState.AuthenticationRequired => "Введите код клиента для подключения Service.",
        ServiceConnectionState.RegistrationRequired => "Для подключения Service зарегистрируйте этот компьютер.",
        ServiceConnectionState.RegistrationPending => "Заявка на подключение Service ожидает подтверждения.",
        ServiceConnectionState.RegistrationRejected => "Заявка на подключение Service отклонена.",
        ServiceConnectionState.NotEntitled => "Service для этого компьютера не подключён.",
        ServiceConnectionState.Expired => "Срок HonestFlow Service истёк.",
        ServiceConnectionState.Disabled => "Service для этой организации или компьютера отключён.",
        ServiceConnectionState.UpdateRequired => "Для подключения Service требуется обновить HonestFlow.",
        ServiceConnectionState.Connecting or ServiceConnectionState.EntitlementChecking => "Проверяем подключение HonestFlow Service…",
        _ => string.IsNullOrWhiteSpace(fallback)
            ? "Не удалось проверить состояние HonestFlow Service."
            : fallback.Trim()
    };
}
