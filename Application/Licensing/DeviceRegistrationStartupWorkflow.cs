using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Auth;

namespace HonestFlow.Application.Licensing
{
    public sealed class DeviceRegistrationStartupWorkflow
    {
        private readonly DeviceRegistrationWorkflow _registrationWorkflow;
        private readonly IDeviceRegistrationStatusProvider _statusProvider;
        private readonly IApiSessionRefresher _sessionRefresher;
        private readonly IApiCredentialAuthService _authentication;

        public DeviceRegistrationStartupWorkflow(
            DeviceRegistrationWorkflow registrationWorkflow,
            IDeviceRegistrationStatusProvider statusProvider,
            IApiSessionRefresher sessionRefresher,
            IApiCredentialAuthService authentication)
        {
            _registrationWorkflow = registrationWorkflow ?? throw new ArgumentNullException(nameof(registrationWorkflow));
            _statusProvider = statusProvider ?? throw new ArgumentNullException(nameof(statusProvider));
            _sessionRefresher = sessionRefresher ?? throw new ArgumentNullException(nameof(sessionRefresher));
            _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        }

        public async Task<DeviceRegistrationStartupResult> SubmitAsync(
            LicenseObservationSnapshot snapshot,
            string address,
            CancellationToken cancellationToken)
        {
            string trimmedAddress = address?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedAddress))
                return DeviceRegistrationStartupResult.InvalidAddress("Введите физический адрес торговой точки.");
            if (trimmedAddress.Length > 300)
                return DeviceRegistrationStartupResult.InvalidAddress("Адрес не должен быть длиннее 300 символов.");

            DeviceRegistrationDeliveryStatus delivery = await _registrationWorkflow.SendAsync(
                snapshot,
                trimmedAddress,
                Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                cancellationToken);
            return delivery switch
            {
                DeviceRegistrationDeliveryStatus.Sent => DeviceRegistrationStartupResult.Pending(
                    "Заявка на регистрацию отправлена. Ожидается обработка."),
                DeviceRegistrationDeliveryStatus.AlreadySent => DeviceRegistrationStartupResult.Pending(
                    "Заявка уже отправлена. Ожидается обработка."),
                _ => DeviceRegistrationStartupResult.SendFailed(
                    "Не удалось отправить заявку. Проверьте адрес и повторите попытку.")
            };
        }

        public async Task<DeviceRegistrationStartupResult> CheckAsync(CancellationToken cancellationToken)
        {
            DeviceRegistrationStatus status;
            try
            {
                status = await _statusProvider.GetCurrentAsync(cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                return DeviceRegistrationStartupResult.StatusUnavailable(
                    "Не удалось получить статус заявки. Попробуйте проверить снова.", ex);
            }

            if (status == null || string.IsNullOrWhiteSpace(status.Status))
                return DeviceRegistrationStartupResult.AwaitingAddress(
                    "Введите физический адрес торговой точки для регистрации устройства.");

            if (string.Equals(status.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                return DeviceRegistrationStartupResult.Pending("Заявка ожидает обработки.");

            if (string.Equals(status.Status, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                string message = string.IsNullOrWhiteSpace(status.Comment)
                    ? "Заявка на регистрацию отклонена."
                    : "Заявка на регистрацию отклонена: " + status.Comment;
                return DeviceRegistrationStartupResult.Rejected(message);
            }

            if (!string.Equals(status.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                return DeviceRegistrationStartupResult.StatusUnavailable(
                    "Сервер вернул неизвестный статус заявки. Попробуйте проверить снова.");

            bool refreshed;
            try
            {
                refreshed = await _sessionRefresher.RefreshSessionAsync(cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                return DeviceRegistrationStartupResult.ApprovedNotReady(
                    "Устройство одобрено, но не удалось обновить сессию. Попробуйте проверить снова.", ex);
            }

            if (!refreshed)
                return DeviceRegistrationStartupResult.ApprovedNotReady(
                    "Устройство одобрено, но сессию пока не удалось обновить. Попробуйте проверить снова.");

            LicenseAuthenticationResult authentication;
            try
            {
                authentication = await _authentication.TryResumeAsync(null, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                return DeviceRegistrationStartupResult.ApprovedNotReady(
                    "Устройство одобрено, но конфигурация или лицензия ещё недоступны. Попробуйте проверить снова.", ex);
            }

            if (authentication?.Client != null &&
                authentication.LicenseSnapshot?.Decision == LicenseDecision.Allowed)
            {
                return DeviceRegistrationStartupResult.Allowed(authentication);
            }

            string unavailableMessage = authentication?.LicenseSnapshot?.Message;
            return DeviceRegistrationStartupResult.ApprovedNotReady(
                string.IsNullOrWhiteSpace(unavailableMessage)
                    ? "Устройство одобрено, но лицензия ещё не готова. Попробуйте проверить снова позже."
                    : unavailableMessage);
        }
    }

    public enum DeviceRegistrationStartupState
    {
        AwaitingAddress,
        InvalidAddress,
        Pending,
        Rejected,
        StatusUnavailable,
        SendFailed,
        ApprovedNotReady,
        Allowed
    }

    public sealed class DeviceRegistrationStartupResult
    {
        private DeviceRegistrationStartupResult(
            DeviceRegistrationStartupState state,
            string message,
            LicenseAuthenticationResult authentication = null,
            Exception exception = null)
        {
            State = state;
            Message = message;
            Authentication = authentication;
            Exception = exception;
        }

        public DeviceRegistrationStartupState State { get; }
        public string Message { get; }
        public LicenseAuthenticationResult Authentication { get; }
        public Exception Exception { get; }
        public bool CanSubmitAddress => State == DeviceRegistrationStartupState.AwaitingAddress ||
                                        State == DeviceRegistrationStartupState.InvalidAddress ||
                                        State == DeviceRegistrationStartupState.SendFailed;

        public static DeviceRegistrationStartupResult AwaitingAddress(string message) =>
            new(DeviceRegistrationStartupState.AwaitingAddress, message);
        public static DeviceRegistrationStartupResult InvalidAddress(string message) =>
            new(DeviceRegistrationStartupState.InvalidAddress, message);
        public static DeviceRegistrationStartupResult Pending(string message) =>
            new(DeviceRegistrationStartupState.Pending, message);
        public static DeviceRegistrationStartupResult Rejected(string message) =>
            new(DeviceRegistrationStartupState.Rejected, message);
        public static DeviceRegistrationStartupResult StatusUnavailable(string message, Exception exception = null) =>
            new(DeviceRegistrationStartupState.StatusUnavailable, message, null, exception);
        public static DeviceRegistrationStartupResult SendFailed(string message) =>
            new(DeviceRegistrationStartupState.SendFailed, message);
        public static DeviceRegistrationStartupResult ApprovedNotReady(string message, Exception exception = null) =>
            new(DeviceRegistrationStartupState.ApprovedNotReady, message, null, exception);
        public static DeviceRegistrationStartupResult Allowed(LicenseAuthenticationResult authentication) =>
            new(DeviceRegistrationStartupState.Allowed, null, authentication);
    }
}
