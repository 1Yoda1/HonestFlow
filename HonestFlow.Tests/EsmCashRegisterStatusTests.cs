using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.PointStatus;
using HonestFlow.Infrastructure.Api;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class EsmCashRegisterStatusTests
    {
        [Fact]
        public async Task SnapshotDataWithKkt_IsConnected()
        {
            string config = CreateConfig();
            try
            {
                using var client = Client(config,
                    "{\"IsAvailable\":true,\"Data\":{\"kkt\":[{\"kktSerial\":\"must-not-be-retained\",\"shiftState\":\"Открыта\"}]}}" );
                var result = await client.GetCashRegisterStatusAsync(CancellationToken.None);
                Assert.Equal(EsmCashRegisterResultKind.Connected, result.Kind);
            }
            finally { File.Delete(config); }
        }

        [Fact]
        public async Task SnapshotNullData_IsDisconnected()
        {
            string config = CreateConfig();
            try
            {
                using var client = Client(config, "{\"IsAvailable\":true,\"Data\":null}");
                var api = await client.GetCashRegisterStatusAsync(CancellationToken.None);
                var status = PointStatusService.BuildKktStatus(AllServices("Running"), api);
                Assert.Equal(EsmCashRegisterResultKind.Disconnected, api.Kind);
                Assert.Equal(NodeLevel.Error, status.Level);
                Assert.Contains("Проверьте подключение", status.StatusText);
                Assert.Contains("физически подключена", status.Details);
                Assert.Contains("ККТ выбрана и доступна в товароучётной системе", status.Details);
                Assert.Contains("перезапустите товароучётную систему", status.Details);
                Assert.False(status.CanManageServices);
            }
            finally { File.Delete(config); }
        }

        [Fact]
        public async Task NativeDkktListData_IsConnected()
        {
            string config = CreateConfig();
            try
            {
                using var client = Client(config, "{\"kkt\":[{}]}");
                Assert.Equal(EsmCashRegisterResultKind.Connected,
                    (await client.GetCashRegisterStatusAsync(CancellationToken.None)).Kind);
            }
            finally { File.Delete(config); }
        }

        [Fact]
        public async Task NoContent_IsDisconnected()
        {
            string config = CreateConfig();
            try
            {
                using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent)));
                using var client = new EsmRestStatusClient(http, config);
                Assert.Equal(EsmCashRegisterResultKind.Disconnected,
                    (await client.GetCashRegisterStatusAsync(CancellationToken.None)).Kind);
            }
            finally { File.Delete(config); }
        }

        [Fact]
        public void RunningServicesAndData_IsGreen()
        {
            var status = PointStatusService.BuildKktStatus(AllServices("Running"), EsmCashRegisterResult.Connected());
            Assert.Equal(NodeLevel.Ok, status.Level);
            Assert.Equal("ККТ подключена", status.StatusText);
        }

        [Fact]
        public void StoppedService_OffersStartAndDoesNotTrustApi()
        {
            var services = new NodeStatus(NodeLevel.Warning, "Не запущено", "uem-agent: Stopped", new[]
            {
                new ServiceSnapshot("uem-agent", "Stopped"),
                new ServiceSnapshot("uem-updater", "Running"),
                new ServiceSnapshot("atol-grpc-service", "Running")
            });
            var status = PointStatusService.BuildKktStatus(services, EsmCashRegisterResult.Connected());
            Assert.Equal(NodeLevel.Error, status.Level);
            Assert.Equal("Запустить", status.ActionText);
        }

        [Fact]
        public void MissingDriverService_RequiresDriverInstallationAndOnlyRefreshes()
        {
            var services = new NodeStatus(NodeLevel.Warning, "Неполный набор", "Найдены две службы", new[]
            {
                new ServiceSnapshot("uem-agent", "Running"),
                new ServiceSnapshot("uem-updater", "Running")
            });

            var status = PointStatusService.BuildKktStatus(services, EsmCashRegisterResult.Connected());

            Assert.Equal(NodeLevel.Error, status.Level);
            Assert.Contains("Драйвер ККТ для работы с ЕСМ не установлен", status.StatusText);
            Assert.False(status.CanManageServices);
            Assert.Equal("Обновить", status.ActionText);
        }

        [Fact]
        public void ApiUnavailable_IsWarning()
        {
            var status = PointStatusService.BuildKktStatus(AllServices("Running"), EsmCashRegisterResult.Unavailable());
            Assert.Equal(NodeLevel.Warning, status.Level);
            Assert.Contains("не получено", status.StatusText);
        }

        private static NodeStatus AllServices(string state) =>
            new(NodeLevel.Ok, "OK", "Все службы запущены", new[]
            {
                new ServiceSnapshot("uem-agent", state),
                new ServiceSnapshot("uem-updater", state),
                new ServiceSnapshot("atol-grpc-service", state)
            });

        private static EsmRestStatusClient Client(string config, string json)
        {
            var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            }));
            return new EsmRestStatusClient(http, config, ownsClient: true);
        }

        private static string CreateConfig()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            File.WriteAllText(path, "{\"port\":51077}");
            return path;
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<CancellationToken, HttpResponseMessage> _response;
            public StubHandler(Func<CancellationToken, HttpResponseMessage> response) => _response = response;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(_response(cancellationToken));
        }
    }
}
