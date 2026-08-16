using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.PointStatus;

namespace HonestFlow.Infrastructure
{
    public sealed class KktPortProbe : IKktPortProbe
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
        private readonly string _host;
        private readonly int _port;
        private readonly TimeSpan _timeout;

        public KktPortProbe()
            : this("127.0.0.1", 4041, Timeout)
        {
        }

        public KktPortProbe(string host, int port, TimeSpan? timeout = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _port = port;
            _timeout = timeout ?? Timeout;
        }

        public async Task<KktPortProbeResult> CheckAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            using var client = new TcpClient();
            try
            {
                await client.ConnectAsync(_host, _port, timeout.Token).ConfigureAwait(false);
                return KktPortProbeResult.Available();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return KktPortProbeResult.Unavailable("timeout");
            }
            catch (SocketException)
            {
                return KktPortProbeResult.Unavailable("refused");
            }
        }
    }
}
