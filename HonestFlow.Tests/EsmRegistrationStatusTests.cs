using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Installation;
using HonestFlow.Application.PointStatus;
using HonestFlow.Infrastructure.Api;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class EsmRegistrationStatusTests
    {
        [Fact]
        public void NotInstalled_IsRed()
        {
            NodeStatus result = Build(services: new NodeStatus(NodeLevel.Error, "missing", "missing"), version: Version(ComponentVersionState.NotInstalled));
            Assert.Equal(NodeLevel.Error, result.Level);
            Assert.Equal("ЕСМ не установлен", result.StatusText);
        }

        [Theory]
        [InlineData("esm-orchestrator")]
        [InlineData("esm-cm-shop-42")]
        public void MissingRequiredService_IsRed(string missing)
        {
            NodeStatus result = Build(services: Services(("esm-orchestrator", "Running"), ("esm-cm-shop-42", "Running")),
                version: Version(ComponentVersionState.Current));
            ServiceSnapshot[] present = Array.FindAll(result.Services.ToArray(), service => !string.Equals(service.ServiceName, missing, StringComparison.OrdinalIgnoreCase));

            result = Build(services: new NodeStatus(NodeLevel.Warning, "partial", "partial", present), version: Version(ComponentVersionState.Current));

            Assert.Equal(NodeLevel.Error, result.Level);
            Assert.Equal("Служба не найдена", result.StatusText);
        }

        [Theory]
        [InlineData("esm-orchestrator")]
        [InlineData("esm-cm-shop-42")]
        public void StoppedRequiredService_IsRed(string stopped)
        {
            NodeStatus result = Build(services: Services(
                ("esm-orchestrator", stopped == "esm-orchestrator" ? "Stopped" : "Running"),
                ("esm-cm-shop-42", stopped == "esm-cm-shop-42" ? "Stopped" : "Running")),
                version: Version(ComponentVersionState.UpdateRequired));

            Assert.Equal(NodeLevel.Error, result.Level);
            Assert.Equal("Служба остановлена", result.StatusText);
        }

        [Fact]
        public void PortUnavailable_IsRed()
        {
            NodeStatus result = Build(port: EsmApiPortProbeResult.Unavailable(51077, "refused"), version: Version(ComponentVersionState.Current));
            Assert.Equal(NodeLevel.Error, result.Level);
            Assert.Equal("ЕСМ недоступен", result.StatusText);
        }

        [Fact]
        public void RegistrationStatusUnavailable_IsRed()
        {
            NodeStatus result = Build(registration: EsmRegistrationResult.Unavailable(), version: Version(ComponentVersionState.Current));
            Assert.Equal(NodeLevel.Error, result.Level);
            Assert.Equal("Статус ЕСМ недоступен", result.StatusText);
        }

        [Fact]
        public void ConfirmedNotRegistered_IsRed()
        {
            NodeStatus result = Build(registration: EsmRegistrationResult.NotConfigured(), version: Version(ComponentVersionState.Current));
            Assert.Equal(NodeLevel.Error, result.Level);
            Assert.Equal("ЕСМ не зарегистрирован", result.StatusText);
        }

        [Fact]
        public void NotRegistered_TakesPriorityOverMissingCmService()
        {
            NodeStatus result = Build(
                services: Services(("esm-orchestrator", "Running")),
                registration: EsmRegistrationResult.NotConfigured(),
                version: Version(ComponentVersionState.Current));

            Assert.Equal(NodeLevel.Error, result.Level);
            Assert.Equal("ЕСМ не зарегистрирован", result.StatusText);
        }

        [Fact]
        public void RegistrationStatusUnavailable_TakesPriorityOverMissingCmService()
        {
            NodeStatus result = Build(
                services: Services(("esm-orchestrator", "Running")),
                registration: EsmRegistrationResult.Unavailable(),
                version: Version(ComponentVersionState.Current));

            Assert.Equal(NodeLevel.Error, result.Level);
            Assert.Equal("Статус ЕСМ недоступен", result.StatusText);
        }

        [Fact]
        public void NotRegisteredWithMissingCm_DoesNotCreateServiceMissingIssue()
        {
            NodeStatus services = Services(("esm-orchestrator", "Running"));
            var result = new PointStatusResult
            {
                Esm = Build(services, registration: EsmRegistrationResult.NotConfigured(), version: Version(ComponentVersionState.Current)),
                EsmServiceStatus = services,
                ServiceSnapshots = services.Services.ToArray(),
                EsmRegistration = EsmRegistrationResult.NotConfigured(),
                EsmApiStatus = EsmStatusResult.Success(new EsmStatusDto
                {
                    Gismt = new EsmComponentStatus { Code = 0 },
                    Lm = new EsmComponentStatus { Code = 0 },
                    LmInfo = new EsmLmInfoDto { Code = 0 }
                }),
                Kkt = new NodeStatus(NodeLevel.Ok, "Доступно", ""),
                Lm = new NodeStatus(NodeLevel.Ok, "Доступно", ""),
                Controller = new NodeStatus(NodeLevel.Ok, "Доступно", ""),
                CashRegister = EsmCashRegisterResult.Connected()
            };

            var issues = new DiagnosticsSnapshotBuilder().Create(result).Issues;

            Assert.Contains(issues, issue => issue.Code == DiagnosticIssueCode.ESM_NOT_REGISTERED);
            Assert.DoesNotContain(issues, issue => issue.Code == DiagnosticIssueCode.ESM_SERVICE_MISSING);
        }

        [Fact]
        public void OlderVersion_IsYellowOnlyAfterTechnicalChecksPass()
        {
            NodeStatus result = Build(version: Version(ComponentVersionState.UpdateRequired));
            Assert.Equal(NodeLevel.Warning, result.Level);
            Assert.Equal("Обновите ЕСМ", result.StatusText);
        }

        [Fact]
        public void CurrentVersionWithHealthyTechnicalChecks_IsGreen()
        {
            NodeStatus result = Build(version: Version(ComponentVersionState.Current));
            Assert.Equal(NodeLevel.Ok, result.Level);
            Assert.Equal("Доступно", result.StatusText);
        }

        [Fact]
        public async Task InstancesWithoutId_AreNotConfigured()
        {
            string config = CreateConfig();
            try
            {
                using var client = Client(config, HttpStatusCode.OK, "{\"instances\":[]}");
                Assert.Equal(EsmRegistrationResultKind.NotConfigured,
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
                using var client = Client(config, HttpStatusCode.OK, "{\"instances\":[{\"id\":\"one\"}]}");
                Assert.Equal(EsmRegistrationResultKind.Registered,
                    (await client.GetRegistrationStatusAsync(CancellationToken.None)).Kind);
            }
            finally { File.Delete(config); }
        }

        private static NodeStatus Build(
            NodeStatus services = null,
            EsmApiPortProbeResult port = null,
            EsmRegistrationResult registration = null,
            ComponentVersionStatus version = null) =>
            PointStatusService.BuildEsmStatus(
                services ?? Services(("esm-orchestrator", "Running"), ("esm-cm-shop-42", "Running")),
                port ?? EsmApiPortProbeResult.Available(51077),
                registration ?? EsmRegistrationResult.Registered(),
                version ?? Version(ComponentVersionState.Current));

        private static NodeStatus Services(params (string Name, string State)[] states) =>
            new(NodeLevel.Ok, "services", "services", Array.ConvertAll(states, item => new ServiceSnapshot(item.Name, item.State)));

        private static ComponentVersionStatus Version(ComponentVersionState state) =>
            new("ЕСМ", state == ComponentVersionState.NotInstalled ? null : "1.6.3.1", "1.6.3.2", state);

        private static EsmRestStatusClient Client(string config, HttpStatusCode status, string json)
        {
            var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(status) { Content = new StringContent(json) }));
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
