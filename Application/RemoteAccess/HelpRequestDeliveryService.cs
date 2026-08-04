using System;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.RemoteAccess
{
    public sealed class HelpRequestDeliveryService
    {
        private readonly HelpRequestEmailSender _sender;
        private readonly UnlicensedHelpRequestStore _store;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public HelpRequestDeliveryService(
            HelpRequestEmailSender sender,
            UnlicensedHelpRequestStore store)
        {
            _sender = sender ?? throw new ArgumentNullException(nameof(sender));
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public async Task SendAsync(
            HelpRequestData request,
            bool hasActiveLicense,
            string clientId,
            string deviceId,
            CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (!hasActiveLicense)
                {
                    if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(deviceId))
                        throw new InvalidOperationException(
                            "Не удалось определить точку и устройство для бесплатной заявки.");

                    if (_store.WasSent(clientId, deviceId))
                        throw new InvalidOperationException(
                            "Бесплатная заявка помощи для этой точки и компьютера уже была отправлена.");
                }

                await _sender.Send(request);
                if (!hasActiveLicense)
                    _store.MarkSent(clientId, deviceId);
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
