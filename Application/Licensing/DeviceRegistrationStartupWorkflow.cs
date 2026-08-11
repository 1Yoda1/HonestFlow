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
        private DeviceRegistrationStartupState? _lastState;
        private bool _isResubmission;

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
                return Remember(DeviceRegistrationStartupResult.InvalidAddress("Введите физический адрес торговой точки."));
            if (trimmedAddress.Length > 300)
                return Remember(DeviceRegistrationStartupResult.InvalidAddress("Адрес не должен быть длиннее 300 символов."));

            bool isResubmission = _isResubmission ||
                                  _lastState == DeviceRegistrationStartupState.Rejected;
            DeviceRegistrationDeliveryStatus delivery = await _registrationWorkflow.SendExplicitAsync(
                snapshot,
                trimmedAddress,
                Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                cancellationToken);
            DeviceRegistrationStartupResult result = delivery switch
            {
                DeviceRegistrationDeliveryStatus.Sent => DeviceRegistrationStartupResult.Pending(
                    isResubmission
                        ? "Заявка отправлена повторно. Ожидается подтверждение."
                        : "Заявка на регистрацию отправлена. Ожидается подтверждение."),
                DeviceRegistrationDeliveryStatus.AlreadySent => DeviceRegistrationStartupResult.Pending(
                    "Заявка уже отправлена. Ожидается обработка."),
                _ => DeviceRegistrationStartupResult.SendFailed(
                    "Не удалось отправить заявку. Проверьте адрес и повторите попытку.")
            };
            if (delivery == DeviceRegistrationDeliveryStatus.Sent)
                _isResubmission = false;
            return Remember(result);
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
                return Remember(DeviceRegistrationStartupResult.StatusUnavailable(
                    "Не удалось получить статус заявки. Попробуйте проверить снова.", ex));
            }

            if (status == null || string.IsNullOrWhiteSpace(status.Status))
            {
                _isResubmission = false;
                return Remember(DeviceRegistrationStartupResult.AwaitingAddress(
                    "Введите физический адрес торговой точки для регистрации устройства."));
            }

            if (string.Equals(status.Status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                _isResubmission = false;
                return Remember(DeviceRegistrationStartupResult.Pending("Заявка ожидает обработки."));
            }

            if (string.Equals(status.Status, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                _isResubmission = true;
                string message = string.IsNullOrWhiteSpace(status.Comment)
                    ? "Заявка на регистрацию отклонена."
                    : "Заявка на регистрацию отклонена: " + status.Comment;
                return Remember(DeviceRegistrationStartupResult.Rejected(message));
            }

            if (!string.Equals(status.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                return Remember(DeviceRegistrationStartupResult.StatusUnavailable(
                    "Сервер вернул неизвестный статус заявки. Попробуйте проверить снова."));

            bool refreshed;
            try
            {
                refreshed = await _sessionRefresher.RefreshSessionAsync(cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                return Remember(DeviceRegistrationStartupResult.ApprovedNotReady(
                    "Устройство одобрено, но не удалось обновить сессию. Попробуйте проверить снова.", ex));
            }

            if (!refreshed)
                return Remember(DeviceRegistrationStartupResult.ApprovedNotReady(
                    "Устройство одобрено, но сессию пока не удалось обновить. Попробуйте проверить снова."));

            LicenseAuthenticationResult authentication;
            try
            {
                authentication = await _authentication.TryResumeAsync(null, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                return Remember(DeviceRegistrationStartupResult.ApprovedNotReady(
                    "Устройство одобрено, но конфигурация или лицензия ещё недоступны. Попробуйте проверить снова.", ex));
            }

            if (authentication?.Client != null &&
                authentication.LicenseSnapshot?.Decision == LicenseDecision.Allowed)
            {
                return Remember(DeviceRegistrationStartupResult.Allowed(authentication));
            }

            string unavailableMessage = authentication?.LicenseSnapshot?.Message;
            return Remember(DeviceRegistrationStartupResult.ApprovedNotReady(
                string.IsNullOrWhiteSpace(unavailableMessage)
                    ? "Устройство одобрено, но лицензия ещё не готова. Попробуйте проверить снова позже."
                    : unavailableMessage));
        }

        private DeviceRegistrationStartupResult Remember(DeviceRegistrationStartupResult result)
        {
            _lastState = result.State;
            return result;
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
                                        State == DeviceRegistrationStartupState.Rejected ||
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
