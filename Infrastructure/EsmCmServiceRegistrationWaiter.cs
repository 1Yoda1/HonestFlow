using System;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Installation;
using HonestFlow.Application.PointStatus;

namespace HonestFlow.Infrastructure
{
    public sealed class EsmCmServiceRegistrationWaiter : IEsmCmServiceRegistrationWaiter
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

        private readonly TimeSpan _timeout;
        private readonly TimeSpan _pollInterval;

        public EsmCmServiceRegistrationWaiter(
            TimeSpan? timeout = null,
            TimeSpan? pollInterval = null)
        {
            _timeout = timeout ?? DefaultTimeout;
            _pollInterval = pollInterval ?? DefaultPollInterval;
        }

        public async Task<bool> WaitForServiceAsync(string registrationId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(registrationId))
                return false;

            string serviceName = EsmCmServiceNameResolver.ServicePrefix + registrationId.Trim();
            DateTime deadlineUtc = DateTime.UtcNow.Add(_timeout);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ServiceExists(serviceName))
                    return true;

                TimeSpan remaining = deadlineUtc - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    return false;

                TimeSpan delay = remaining < _pollInterval ? remaining : _pollInterval;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        private static bool ServiceExists(string serviceName)
        {
            try
            {
                using var service = new ServiceController(serviceName);
                service.Refresh();
                _ = service.Status;
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}
