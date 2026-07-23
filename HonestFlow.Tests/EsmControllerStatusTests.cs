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
    public sealed class EsmControllerStatusTests
    {
        [Fact]
        public void StoppedService_IsError()
        {
            var service = ServiceStatus("Stopped");
            var result = PointStatusService.BuildControllerStatus(service, null, lmReady: true, DateTime.Now);
            Assert.Equal(NodeLevel.Error, result.Level);
            Assert.Equal("Служба остановлена", result.StatusText);
        }

        [Fact]
        public async Task MissingConfig_IsNotConfigured()
        {
            using var client = new EsmRestStatusClient(new HttpClient(new StubHandler((Func<CancellationToken, HttpResponseMessage>)(_ => throw new Exception()))), Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
            Assert.Equal(EsmStatusResultKind.NotConfigured, (await client.GetStatusAsync(CancellationToken.None)).Kind);
        }

        [Fact]
        public async Task ApiUnavailable_IsUnavailable()
        {
            string config = CreateConfig();
            try
            {
                using var client = new EsmRestStatusClient(new HttpClient(new StubHandler((Func<CancellationToken, HttpResponseMessage>)(_ => throw new HttpRequestException()))), config);
                Assert.Equal(EsmStatusResultKind.Unavailable, (await client.GetStatusAsync(CancellationToken.None)).Kind);
            }
            finally { File.Delete(config); }
        }

        [Fact]
        public async Task InstancesNoContent_IsNotConfigured()
        {
            string config = CreateConfig();
            try
            {
                using var client = Client(config, _ => new HttpResponseMessage(HttpStatusCode.NoContent));
                Assert.Equal(EsmStatusResultKind.NotConfigured, (await client.GetStatusAsync(CancellationToken.None)).Kind);
            }
            finally { File.Delete(config); }
        }

        [Fact]
        public void HealthyController_IsGreen()
        {
            DateTime now = new(2026, 7, 19, 23, 22, 0);
            var result = PointStatusService.BuildControllerStatus(ServiceStatus("Running"), Success(0, "", now.AddMinutes(-1)), lmReady: true, now);
            Assert.Equal(NodeLevel.Ok, result.Level);
            Assert.Equal("Контроллер работает", result.StatusText);
        }

        [Fact]
        public void ControllerError2025_IsRed_AndOffersServiceRestart()
        {
            DateTime now = DateTime.Now;
            var result = PointStatusService.BuildControllerStatus(
                ServiceStatus("Running"),
                Success(2025, "нет связи между ЛМ ЧЗ и ЕСМ", now),
                lmReady: true,
                now);

            Assert.Equal(NodeLevel.Error, result.Level);
            Assert.Contains("нет связи", result.StatusText);
            Assert.True(result.CanManageServices);
            Assert.Equal("Перезапуск", result.ActionText);
            Assert.Contains("lmController.code: 2025", result.Details);
            Assert.Contains("Возраст последней связи:", result.Details);
            Assert.Contains("Итог: Ошибка контроллера", result.Details);
        }

        [Fact]
        public void StaleConnection_IsInformationalOnly()
        {
            DateTime now = DateTime.Now;
            var result = PointStatusService.BuildControllerStatus(ServiceStatus("Running"), Success(0, "", now.AddDays(-1)), lmReady: true, now);
            Assert.Equal(NodeLevel.Ok, result.Level);
            Assert.Contains("Возраст последней связи:", result.Details);
        }

        [Fact]
        public void LmDataPresent_IsEnoughEvenWhenInnerStatusIsEmpty()
        {
            DateTime now = DateTime.Now;
            var api = Success(0, "", now.AddMinutes(-1), new EsmLmStatusDto());
            var result = PointStatusService.BuildControllerStatus(ServiceStatus("Running"), api, lmReady: true, now);
            Assert.Equal(NodeLevel.Ok, result.Level);
            Assert.Contains("LM.Data: получены", result.Details);
        }

        [Fact]
        public void MissingLmData_IsRed()
        {
            DateTime now = DateTime.Now;
            var api = Success(0, "", now.AddMinutes(-1));
            api.Status.LmInfo = null;
            var result = PointStatusService.BuildControllerStatus(ServiceStatus("Running"), api, lmReady: true, now);
            Assert.Equal(NodeLevel.Error, result.Level);
            Assert.Contains("не получил данные ЛМ", result.StatusText);
            Assert.Equal("Перезапуск", result.ActionText);
        }

        [Fact]
        public void MissingLmData_WhenPort5995IsNotReady_PointsToLmWithoutControllerRestart()
        {
            DateTime now = DateTime.Now;
            var api = Success(0, "", now.AddMinutes(-1));
            api.Status.LmInfo = null;

            var result = PointStatusService.BuildControllerStatus(
                ServiceStatus("Running"),
                api,
                lmReady: false,
                now);

            Assert.Equal(NodeLevel.Warning, result.Level);
            Assert.Contains("Сначала восстановите ЛМ ЧЗ", result.StatusText);
            Assert.False(result.CanManageServices);
            Assert.Equal("Обновить", result.ActionText);
        }

        [Fact]
        public void LmDataPresent_WhenPort5995IsNotReady_DoesNotBlameController()
        {
            DateTime now = DateTime.Now;
            var result = PointStatusService.BuildControllerStatus(
                ServiceStatus("Running"),
                Success(0, "", now.AddMinutes(-1)),
                lmReady: false,
                now);

            Assert.Equal(NodeLevel.Warning, result.Level);
            Assert.Contains("ЛМ ЧЗ не готова", result.StatusText);
            Assert.False(result.CanManageServices);
        }

        [Fact]
        public async Task Cancellation_IsPropagated()
        {
            string config = CreateConfig();
            try
            {
                using var client = Client(config, async token => { await Task.Delay(Timeout.Infinite, token); return new HttpResponseMessage(HttpStatusCode.OK); });
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetStatusAsync(cancellation.Token));
            }
            finally { File.Delete(config); }
        }

        [Fact]
        public async Task UnknownJsonFieldsAreIgnored_AndMissingControllerFieldsAreSafe()
        {
            string config = CreateConfig();
            try
            {
                int request = 0;
                using var client = Client(config, _ => Json(request++ switch
                {
                    0 => "{\"instances\":[{\"id\":\"one\",\"secret\":\"ignored\"}],\"extra\":1}",
                    1 => "{\"lmController\":{\"extra\":true},\"clientSoftware\":{\"id\":\"ignored\"}}",
                    _ => "{\"lmStatus\":{},\"unknown\":true}"
                }));
                var api = await client.GetStatusAsync(CancellationToken.None);
                var result = PointStatusService.BuildControllerStatus(ServiceStatus("Running"), api, lmReady: true, DateTime.Now);
                Assert.Equal(EsmStatusResultKind.Success, api.Kind);
                Assert.Equal(NodeLevel.Warning, result.Level); // lmController.code is intentionally missing.
            }
            finally { File.Delete(config); }
        }

        [Fact]
        public async Task EmptyLmArray_IsSuccessfulResponseWithMissingLmData()
        {
            string config = CreateConfig();
            try
            {
                int request = 0;
                using var client = Client(config, _ => Json(request++ switch
                {
                    0 => "{\"instances\":[{\"id\":\"one\"}]}",
                    1 => "{\"lmController\":{\"code\":0,\"error\":\"\",\"lastConnection\":\"12:00:00 23-07-2026\"}}",
                    _ => "[]"
                }));

                var api = await client.GetStatusAsync(CancellationToken.None);

                Assert.Equal(EsmStatusResultKind.Success, api.Kind);
                Assert.Null(api.Status.LmInfo);
            }
            finally { File.Delete(config); }
        }

        private static NodeStatus ServiceStatus(string state) =>
            new(state == "Running" ? NodeLevel.Ok : NodeLevel.Warning, state, state, new[] { new ServiceSnapshot("esm-lm-controller", state) });

        private static EsmStatusResult Success(int code, string error, DateTime lastConnection, EsmLmStatusDto lmStatus = null) =>
            EsmStatusResult.Success(new EsmStatusDto
            {
                LmController = new EsmComponentStatus { Code = code, Error = error, LastConnection = lastConnection.ToString("HH:mm:ss dd-MM-yyyy") },
                LmInfo = new EsmLmInfoDto
                {
                    ControllerVersion = "1.6.3.2",
                    Code = 0,
                    LmStatus = lmStatus ?? new EsmLmStatusDto { Status = "ready", OperationMode = "active", Version = "2.5.1-2", LastSync = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
                }
            });

        private static string CreateConfig()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            File.WriteAllText(path, "{\"port\":51077,\"ignored\":true}");
            return path;
        }

        private static EsmRestStatusClient Client(string config, Func<CancellationToken, HttpResponseMessage> response) =>
            new(new HttpClient(new StubHandler(response)), config);

        private static EsmRestStatusClient Client(string config, Func<CancellationToken, Task<HttpResponseMessage>> response) =>
            new(new HttpClient(new StubHandler(response)), config);

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json) };

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<CancellationToken, Task<HttpResponseMessage>> _response;
            public StubHandler(Func<CancellationToken, HttpResponseMessage> response) => _response = token => Task.FromResult(response(token));
            public StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> response) => _response = response;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _response(cancellationToken);
        }
    }
}
