using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.PointStatus;
using HonestFlow.Infrastructure.Api;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class EsmNodeIndependenceTests
    {
        [Fact]
        public void DownstreamFailures_DoNotChangeHealthyEsmNode()
        {
            PointStatusResult result = Healthy();
            result.EsmApiStatus.Status.Gismt.Code = 5;
            result.EsmApiStatus.Status.Lm.Code = 5;
            result.EsmApiStatus.Status.LmInfo.Code = 5;
            result.CashRegister = EsmCashRegisterResult.Disconnected();

            DiagnosticsSnapshot snapshot = new DiagnosticsSnapshotBuilder().Create(result);

            Assert.Equal(DiagnosticState.Healthy, snapshot.Esm.State);
        }

        [Fact]
        public async Task PortProbe_ReadsConfiguredPortFromTemporarySettings()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string settings = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            File.WriteAllText(settings, $"{{\"port\":{port}}}");
            try
            {
                var probe = new EsmApiPortProbe(settings, TimeSpan.FromSeconds(1));
                EsmApiPortProbeResult result = await probe.CheckAsync(CancellationToken.None);

                Assert.True(result.IsAvailable);
                Assert.Equal(port, result.Port);
            }
            finally
            {
                listener.Stop();
                File.Delete(settings);
            }
        }

        private static PointStatusResult Healthy() => new()
        {
            Esm = new NodeStatus(NodeLevel.Ok, "Доступно", "ESM component only"),
            Kkt = new NodeStatus(NodeLevel.Ok, "Доступно", "KKT"),
            Controller = new NodeStatus(NodeLevel.Ok, "Доступно", "Controller"),
            Lm = new NodeStatus(NodeLevel.Ok, "Доступно", "LM"),
            EsmApiStatus = EsmStatusResult.Success(new EsmStatusDto
            {
                Gismt = new EsmComponentStatus { Code = 0 },
                Lm = new EsmComponentStatus { Code = 0 },
                LmInfo = new EsmLmInfoDto { Code = 0 }
            }),
            EsmRegistration = EsmRegistrationResult.Registered(),
            CashRegister = EsmCashRegisterResult.Connected()
        };
    }
}
