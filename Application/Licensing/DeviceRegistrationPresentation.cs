using System;

namespace HonestFlow.Application.Licensing
{
    public sealed class DeviceRegistrationPresentation
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public string ClientName { get; init; }
        public string RequestedAtText { get; init; }
        public string StatusText { get; init; }
        public string RejectionReason { get; init; }
        public string GuidanceText { get; init; }
        public bool ShowAddressEntry { get; init; }
        public bool ShowSubmit { get; init; }
        public string SubmitText { get; init; }
        public bool ShowCheckStatus { get; init; }
        public bool ShowSwitchClient { get; init; }
        public bool HasContext => !string.IsNullOrWhiteSpace(ClientName) ||
                                  !string.IsNullOrWhiteSpace(RequestedAtText) ||
                                  !string.IsNullOrWhiteSpace(StatusText) ||
                                  !string.IsNullOrWhiteSpace(RejectionReason);
    }

    public static class DeviceRegistrationPresentationMapper
    {
        public static DeviceRegistrationPresentation Create(
            DeviceRegistrationStartupResult result,
            string clientName)
        {
            DeviceRegistrationStatus status = result?.RegistrationStatus;
            string requestedAt = status?.RequestedAtUtc == default
                ? null
                : status.RequestedAtUtc.ToLocalTime().ToString("g");

            return result?.State switch
            {
                DeviceRegistrationStartupState.Pending => new()
                {
                    Title = "Регистрация устройства",
                    Description = "Заявка отправлена и ожидает подтверждения.",
                    ClientName = clientName,
                    RequestedAtText = requestedAt,
                    StatusText = "Ожидает подтверждения",
                    GuidanceText = "После подтверждения HonestFlow продолжит запуск автоматически.",
                    ShowCheckStatus = true
                },
                DeviceRegistrationStartupState.Rejected => new()
                {
                    Title = "Регистрация устройства",
                    Description = "Заявка была отклонена. Проверьте данные и отправьте её повторно.",
                    ClientName = clientName,
                    RequestedAtText = requestedAt,
                    StatusText = "Отклонена",
                    RejectionReason = GetRejectionReason(status?.Comment),
                    ShowAddressEntry = true,
                    ShowSubmit = true,
                    SubmitText = "Отправить повторно",
                    ShowSwitchClient = true
                },
                DeviceRegistrationStartupState.StatusUnavailable => new()
                {
                    Title = "Регистрация устройства",
                    Description = "Сейчас не удалось получить текущий статус регистрации.",
                    ClientName = clientName,
                    GuidanceText = string.IsNullOrWhiteSpace(result.Message)
                        ? "Не удалось получить статус заявки. Попробуйте проверить снова."
                        : result.Message,
                    ShowCheckStatus = true,
                    ShowSwitchClient = true
                },
                _ => new()
                {
                    Title = "Регистрация устройства",
                    Description = "Укажите физический адрес торговой точки. Имя компьютера будет передано автоматически.",
                    ClientName = clientName,
                    ShowAddressEntry = true,
                    ShowSubmit = true,
                    SubmitText = "Отправить заявку",
                    ShowSwitchClient = true
                }
            };
        }

        private static string GetRejectionReason(string comment) =>
            string.IsNullOrWhiteSpace(comment) ||
            string.Equals(comment.Trim(), "Отклонено оператором HonestDesk", StringComparison.OrdinalIgnoreCase)
                ? "Заявка отклонена оператором."
                : comment.Trim();
    }
}
