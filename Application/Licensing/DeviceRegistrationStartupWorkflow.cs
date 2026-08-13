using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Auth;
using HonestFlow.Infrastructure.Api;

namespace HonestFlow.Application.Licensing
{
    public sealed class DeviceRegistrationStartupWorkflow
    {
        private readonly DeviceRegistrationWorkflow _registrationWorkflow;
        private readonly IDeviceRegistrationStatusProvider _statusProvider;
        private readonly IApiSessionRefresher _sessionRefresher;
        private readonly IApiCredentialAuthService _authentication;
        private readonly IApiSessionPersistenceController _persistenceController;
        private readonly bool _resumedContinuation;
        private DeviceRegistrationStartupState? _lastState;
        private bool _isResubmission;

        public DeviceRegistrationStartupWorkflow(
            DeviceRegistrationWorkflow registrationWorkflow,
            IDeviceRegistrationStatusProvider statusProvider,
            IApiSessionRefresher sessionRefresher,
            IApiCredentialAuthService authentication,
            IApiSessionPersistenceController persistenceController = null,
            bool resumedContinuation = false)
        {
            _registrationWorkflow = registrationWorkflow ?? throw new ArgumentNullException(nameof(registrationWorkflow));
            _statusProvider = statusProvider ?? throw new ArgumentNullException(nameof(statusProvider));
            _sessionRefresher = sessionRefresher ?? throw new ArgumentNullException(nameof(sessionRefresher));
            _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
            _persistenceController = persistenceController;
            _resumedContinuation = resumedContinuation;
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
            {
                _isResubmission = false;
                await PersistContinuationAsync(cancellationToken);
            }
            else if (delivery == DeviceRegistrationDeliveryStatus.AlreadySent)
            {
                await PersistContinuationAsync(cancellationToken);
            }
            return Remember(result);
        }

        public async Task<DeviceRegistrationStartupResult> CheckAsync(CancellationToken cancellationToken)
        {
            DeviceRegistrationStatus status;
            try
            {
                status = await _statusProvider.GetCurrentAsync(cancellationToken);
            }
            catch (ApiRequestException ex) when (!cancellationToken.IsCancellationRequested &&
                                                (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                                                 ex.StatusCode == System.Net.HttpStatusCode.Forbidden))
            {
                await ClearContinuationAsync(CancellationToken.None);
                return Remember(DeviceRegistrationStartupResult.SessionInvalid(
                    "Сессия продолжения регистрации больше недействительна. Войдите снова.", ex));
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                return Remember(DeviceRegistrationStartupResult.StatusUnavailable(
                    "Не удалось получить статус заявки. Попробуйте проверить снова.", ex));
            }

            if (status == null || string.IsNullOrWhiteSpace(status.Status))
            {
                _isResubmission = false;
                await ClearContinuationAsync(CancellationToken.None);
                if (_resumedContinuation)
                {
                    return Remember(DeviceRegistrationStartupResult.SessionInvalid(
                        "Заявка на регистрацию больше не существует. Войдите снова."));
                }
                return Remember(DeviceRegistrationStartupResult.AwaitingAddress(
                    "Введите физический адрес торговой точки для регистрации устройства."));
            }

            if (string.Equals(status.Status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                _isResubmission = false;
                await PersistContinuationAsync(cancellationToken);
                return Remember(DeviceRegistrationStartupResult.Pending("Заявка ожидает обработки.", status));
            }

            if (string.Equals(status.Status, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                _isResubmission = true;
                string message = string.IsNullOrWhiteSpace(status.Comment)
                    ? "Заявка на регистрацию отклонена."
                    : "Заявка на регистрацию отклонена: " + status.Comment;
                await PersistContinuationAsync(cancellationToken);
                return Remember(DeviceRegistrationStartupResult.Rejected(message, status));
            }

            if (!string.Equals(status.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                return Remember(DeviceRegistrationStartupResult.StatusUnavailable(
                    "Сервер вернул неизвестный статус заявки. Попробуйте проверить снова."));

            bool refreshed;
            try
            {
                _persistenceController?.PrepareForRegistrationCompletion();
                refreshed = await _sessionRefresher.RefreshSessionAsync(cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                return Remember(DeviceRegistrationStartupResult.ApprovedNotReady(
                    "Устройство одобрено, но не удалось обновить сессию. Попробуйте проверить снова.",
                    exception: ex));
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
                    "Устройство одобрено, но конфигурация или лицензия ещё недоступны. Попробуйте проверить снова.",
                    exception: ex));
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
                    : unavailableMessage,
                authentication));
        }

        private Task PersistContinuationAsync(CancellationToken cancellationToken) =>
            _persistenceController?.PersistRegistrationContinuationAsync(cancellationToken) ?? Task.CompletedTask;

        private Task ClearContinuationAsync(CancellationToken cancellationToken) =>
            _persistenceController?.ClearRegistrationContinuationAsync(cancellationToken) ?? Task.CompletedTask;

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
        SessionInvalid,
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
        public DeviceRegistrationStatus RegistrationStatus { get; private set; }
        public bool CanSubmitAddress => State == DeviceRegistrationStartupState.AwaitingAddress ||
                                        State == DeviceRegistrationStartupState.InvalidAddress ||
                                        State == DeviceRegistrationStartupState.Rejected ||
                                        State == DeviceRegistrationStartupState.SendFailed;

        public static DeviceRegistrationStartupResult AwaitingAddress(string message) =>
            new(DeviceRegistrationStartupState.AwaitingAddress, message);
        public static DeviceRegistrationStartupResult InvalidAddress(string message) =>
            new(DeviceRegistrationStartupState.InvalidAddress, message);
        public static DeviceRegistrationStartupResult Pending(string message, DeviceRegistrationStatus status = null) =>
            new(DeviceRegistrationStartupState.Pending, message) { RegistrationStatus = status };
        public static DeviceRegistrationStartupResult Rejected(string message, DeviceRegistrationStatus status = null) =>
            new(DeviceRegistrationStartupState.Rejected, message) { RegistrationStatus = status };
        public static DeviceRegistrationStartupResult StatusUnavailable(string message, Exception exception = null) =>
            new(DeviceRegistrationStartupState.StatusUnavailable, message, null, exception);
        public static DeviceRegistrationStartupResult SendFailed(string message) =>
            new(DeviceRegistrationStartupState.SendFailed, message);
        public static DeviceRegistrationStartupResult ApprovedNotReady(
            string message, LicenseAuthenticationResult authentication = null, Exception exception = null) =>
            new(DeviceRegistrationStartupState.ApprovedNotReady, message, authentication, exception);
        public static DeviceRegistrationStartupResult SessionInvalid(string message, Exception exception = null) =>
            new(DeviceRegistrationStartupState.SessionInvalid, message, null, exception);
        public static DeviceRegistrationStartupResult Allowed(LicenseAuthenticationResult authentication) =>
            new(DeviceRegistrationStartupState.Allowed, null, authentication);
    }
}
