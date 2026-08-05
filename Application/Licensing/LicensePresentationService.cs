using System;
using HonestFlow.Models;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicensePresentationService
    {
        public bool IsForClient(IPData client, LicenseObservationSnapshot snapshot) =>
            client != null &&
            snapshot != null &&
            !string.IsNullOrWhiteSpace(client.ClientId) &&
            string.Equals(client.ClientId, snapshot.ClientId, StringComparison.Ordinal);

        public LicenseDecisionPresentation Create(LicenseObservationSnapshot snapshot)
        {
            if (snapshot == null)
                return null;

            return snapshot.Decision switch
            {
                LicenseDecision.Allowed => LicenseDecisionPresentation.Status(
                    "Лицензия проверена. Доступные функции применены."),
                LicenseDecision.DeviceNotRegistered => LicenseDecisionPresentation.Status(
                    "Устройство не зарегистрировано. Заявка на регистрацию отправляется автоматически."),
                LicenseDecision.ClientDisabled => LicenseDecisionPresentation.Warning(
                    "Клиент отключён",
                    "Лицензия клиента отключена. Доступны диагностика и отправка логов."),
                LicenseDecision.DeviceDisabled => LicenseDecisionPresentation.Warning(
                    "Устройство отключено",
                    "Это устройство отключено в лицензии. Доступны диагностика и отправка логов."),
                LicenseDecision.VersionTooOld => LicenseDecisionPresentation.Warning(
                    "Требуется обязательное обновление",
                    $"Текущая версия HonestFlow устарела. Обновите программу до версии {snapshot.MinimumRequiredVersion?.ToString() ?? "указанной в лицензии"} или новее. До обновления доступны только диагностические функции."),
                LicenseDecision.OfflineGraceExpired => LicenseDecisionPresentation.Warning(
                    "Истёк автономный период",
                    $"Offline grace period истёк. Последняя успешная онлайн-проверка: {FormatLastCheck(snapshot)}. Доступны диагностика и отправка логов."),
                LicenseDecision.InvalidLicenseState => LicenseDecisionPresentation.Warning(
                    "Диагностический режим",
                    "Состояние лицензии не удалось надёжно определить. HonestFlow продолжит работу в безопасном диагностическом режиме."),
                _ => LicenseDecisionPresentation.Warning(
                    "Ограниченный режим",
                    string.IsNullOrWhiteSpace(snapshot.Message)
                        ? "Лицензия не разрешает изменяющие систему операции. Доступны диагностические функции."
                        : snapshot.Message)
            };
        }

        private static string FormatLastCheck(LicenseObservationSnapshot snapshot) =>
            snapshot.LastSuccessfulOnlineCheckUtc.HasValue
                ? snapshot.LastSuccessfulOnlineCheckUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss")
                : "неизвестно";
    }

    public sealed class LicenseDecisionPresentation
    {
        private LicenseDecisionPresentation(string title, string message, bool isWarning)
        {
            Title = title;
            Message = message;
            IsWarning = isWarning;
        }

        public string Title { get; }
        public string Message { get; }
        public bool IsWarning { get; }

        public static LicenseDecisionPresentation Status(string message) => new(null, message, false);
        public static LicenseDecisionPresentation Warning(string title, string message) => new(title, message, true);
    }
}
