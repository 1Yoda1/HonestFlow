using System;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Core;
using HonestFlow.Models;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseRefreshWorkflow
    {
        private readonly IAuthService _authService;
        private readonly ILogService _log;

        public LicenseRefreshWorkflow(IAuthService authService, ILogService log)
        {
            _authService = authService;
            _log = log;
        }

        public bool IsAvailable => _authService is ILicenseObservationRefresher;

        public async Task<LicenseObservationSnapshot> RefreshAsync(
            IPData client,
            IProgress<LicenseAuthenticationProgress> progress,
            CancellationToken cancellationToken)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (_authService is not ILicenseObservationRefresher refresher)
                throw new InvalidOperationException("Повторная проверка лицензии недоступна.");

            return await refresher.RefreshLicenseAsync(client, progress, cancellationToken);
        }

        public async Task RunPeriodicAsync(
            Func<IPData> currentClient,
            CancellationToken cancellationToken)
        {
            if (_authService is not ILicenseObservationRefresher)
                return;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(
                        TimeSpan.FromMinutes(60 + Random.Shared.NextDouble() * 15),
                        cancellationToken);
                    IPData client = currentClient();
                    if (client == null)
                        continue;

                    LicenseObservationSnapshot snapshot = await RefreshAsync(
                        client,
                        null,
                        cancellationToken);
                    _log?.LogDebug(
                        $"Event=PeriodicLicenseRefresh Decision={snapshot?.Decision} TechnicalCode={snapshot?.TechnicalCode}");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _log?.LogDebug($"Event=PeriodicLicenseRefresh Status=Failed ErrorType={ex.GetType().Name}");
                }
            }
        }
    }
}
