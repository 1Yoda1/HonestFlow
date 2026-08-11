using System;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Auth;
using HonestFlow.Models;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseNotIssuedStartupWorkflow
    {
        public const string WaitingMessage =
            "Устройство зарегистрировано. Лицензия для этого компьютера ещё не выдана.";

        private readonly ILicenseObservationRefresher _licenseRefresher;
        private readonly IPData _client;

        public LicenseNotIssuedStartupWorkflow(
            ILicenseObservationRefresher licenseRefresher,
            IPData client)
        {
            _licenseRefresher = licenseRefresher ?? throw new ArgumentNullException(nameof(licenseRefresher));
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task<LicenseNotIssuedStartupResult> CheckAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                LicenseObservationSnapshot snapshot = await _licenseRefresher.RefreshLicenseAsync(
                    _client,
                    null,
                    cancellationToken);
                if (snapshot?.Decision == LicenseDecision.Allowed)
                    return LicenseNotIssuedStartupResult.Allowed(
                        new LicenseAuthenticationResult(_client, snapshot));

                return LicenseNotIssuedStartupResult.Restricted(
                    snapshot,
                    snapshot?.Decision == LicenseDecision.LicenseNotIssued ||
                    string.IsNullOrWhiteSpace(snapshot?.Message)
                        ? WaitingMessage
                        : snapshot.Message);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                return LicenseNotIssuedStartupResult.Unavailable(
                    "Не удалось проверить лицензию. Попробуйте проверить снова.",
                    ex);
            }
        }
    }

    public sealed class LicenseNotIssuedStartupResult
    {
        private LicenseNotIssuedStartupResult(
            bool isAllowed,
            string message,
            LicenseObservationSnapshot snapshot,
            LicenseAuthenticationResult authentication,
            Exception exception)
        {
            IsAllowed = isAllowed;
            Message = message;
            Snapshot = snapshot;
            Authentication = authentication;
            Exception = exception;
        }

        public bool IsAllowed { get; }
        public string Message { get; }
        public LicenseObservationSnapshot Snapshot { get; }
        public LicenseAuthenticationResult Authentication { get; }
        public Exception Exception { get; }

        public static LicenseNotIssuedStartupResult Allowed(LicenseAuthenticationResult authentication) =>
            new(true, null, authentication?.LicenseSnapshot, authentication, null);

        public static LicenseNotIssuedStartupResult Restricted(
            LicenseObservationSnapshot snapshot,
            string message) =>
            new(false, message, snapshot, null, null);

        public static LicenseNotIssuedStartupResult Unavailable(string message, Exception exception) =>
            new(false, message, null, null, exception);
    }
}
