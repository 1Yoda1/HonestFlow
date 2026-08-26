using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.PointStatus;

namespace HonestFlow.Infrastructure.Api
{
    public sealed class EsmApiPortProbe : IEsmApiPortProbe
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
        private readonly string _settingsPath;
        private readonly TimeSpan _timeout;
        private readonly int _fallbackPort;

        public EsmApiPortProbe(
            string settingsPath = EsmRestStatusClient.DefaultSettingsPath,
            TimeSpan? timeout = null,
            int fallbackPort = EsmLocalApiSettings.DefaultPort)
        {
            _settingsPath = settingsPath ?? throw new ArgumentNullException(nameof(settingsPath));
            _timeout = timeout ?? DefaultTimeout;
            _fallbackPort = fallbackPort;
        }

        public async Task<EsmApiPortProbeResult> CheckAsync(CancellationToken cancellationToken)
        {
            int port = EsmLocalApiSettings.TryReadPort(_settingsPath) ?? _fallbackPort;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            using var client = new TcpClient();
            try
            {
                await client.ConnectAsync("127.0.0.1", port, timeout.Token).ConfigureAwait(false);
                return EsmApiPortProbeResult.Available(port);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return EsmApiPortProbeResult.Unavailable(port, "timeout");
            }
            catch (SocketException)
            {
                return EsmApiPortProbeResult.Unavailable(port, "refused");
            }
        }
    }
}
