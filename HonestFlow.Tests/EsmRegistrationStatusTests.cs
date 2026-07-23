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
    public sealed class EsmRegistrationStatusTests
    {
        [Fact]
        public void StoppedEsmService_OffersStartBeforeApiCheck()
        {
            var services = EsmServices("Stopped", "Running");
            var result = PointStatusService.BuildEsmStatus(
                services,
                EsmRegistrationResult.Registered(),
                EsmCashRegisterResult.Connected());

            Assert.Equal(NodeLevel.Error, result.Level);
            Assert.Equal("Запустить", result.ActionText);
        }

        [Fact]
        public void NotConfiguredWithConnectedCashRegister_ShowsRegistrationInstruction()
        {
            var result = PointStatusService.BuildEsmStatus(
                EsmServices("Running", "Running"),
                EsmRegistrationResult.NotConfigured(),
                EsmCashRegisterResult.Connected());

            Assert.Equal(NodeLevel.Error, result.Level);
            Assert.Contains("нажмите «Зарегистрировать»", result.StatusText);
            Assert.Contains("CashRegister.Data: получены", result.Details);
            Assert.False(result.CanManageServices);
        }

        [Fact]
        public void NotConfiguredWithoutCashRegister_IsWarning()
        {
            var result = PointStatusService.BuildEsmStatus(
                EsmServices("Running", "Running"),
                EsmRegistrationResult.NotConfigured(),
                EsmCashRegisterResult.Disconnected());

            Assert.Equal(NodeLevel.Warning, result.Level);
            Assert.Contains("ЕСМ не зарегистрирован", result.StatusText);
            Assert.Contains("ККТ", result.StatusText);
        }

        [Fact]
        public void RegisteredWithRunningServices_IsGreen()
        {
            var result = PointStatusService.BuildEsmStatus(
                EsmServices("Running", "Running"),
                EsmRegistrationResult.Registered(),
                EsmCashRegisterResult.Connected());

            Assert.Equal(NodeLevel.Ok, result.Level);
            Assert.Equal("ЕСМ зарегистрирован", result.StatusText);
        }

        [Fact]
        public void MissingBothServices_RequiresEsmInstallationAndOnlyRefreshes()
        {
            var services = new NodeStatus(NodeLevel.Error, "Не найдено", "Службы отсутствуют");
            var result = PointStatusService.BuildEsmStatus(
                services,
                EsmRegistrationResult.NotConfigured(),
                EsmCashRegisterResult.Disconnected());

            Assert.Equal(NodeLevel.Error, result.Level);
            Assert.Contains("Установите ЕСМ", result.StatusText);
            Assert.False(result.CanManageServices);
            Assert.Equal("Обновить", result.ActionText);
        }

        [Fact]
        public async Task InstancesWithoutId_AreNotConfigured()
        {
            string config = CreateConfig();
            try
            {
                using var client = Client(config, HttpStatusCode.OK, "{\"instances\":[]}");
                Assert.Equal(
                    EsmRegistrationResultKind.NotConfigured,
                    (await client.GetRegistrationStatusAsync(CancellationToken.None)).Kind);
            }
            finally { File.Delete(config); }
        }

        [Fact]
        public async Task InstanceWithId_IsRegistered()
        {
            string config = CreateConfig();
            try
            {
                using var client = Client(config, HttpStatusCode.OK, "{\"instances\":[{\"id\":\"one\",\"extra\":\"ignored\"}]}");
                Assert.Equal(
                    EsmRegistrationResultKind.Registered,
                    (await client.GetRegistrationStatusAsync(CancellationToken.None)).Kind);
            }
            finally { File.Delete(config); }
        }

        private static NodeStatus EsmServices(string orchestrator, string controlModule) =>
            new(NodeLevel.Ok, "services", "services", new[]
            {
                new ServiceSnapshot("esm-orchestrator", orchestrator),
                new ServiceSnapshot("esm-cm-test", controlModule)
            });

        private static EsmRestStatusClient Client(string config, HttpStatusCode status, string json)
        {
            var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(status)
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
