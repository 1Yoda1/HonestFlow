using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using HonestFlow.Application.Core;
using HonestFlow.Application.Installation;
using HonestFlow.Application.PointStatus;
using HonestFlow.Models;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class PointStatusRefreshServiceTests
    {
        [Fact]
        public async Task RefreshAsync_ReturnsOnePresentationIndependentSnapshot()
        {
            var pointStatus = new PointStatusResult
            {
                Lm = Status(NodeLevel.Ok, "ЛМ работает"),
                Controller = Status(NodeLevel.Warning, "Нужна проверка"),
                Esm = Status(NodeLevel.Ok, "ЕСМ работает"),
                Kkt = Status(NodeLevel.Ok, "ККТ работает"),
                Cloud = Status(NodeLevel.Ok, "Облако доступно"),
                RuDesktop = Status(NodeLevel.Ok, "RuDesktop работает")
            };
            var service = CreateService(pointStatus);

            PointStatusRefreshResult result = await service.RefreshAsync(
                new IPData(),
                new VersionsData(),
                includeLicensedComponents: true,
                CancellationToken.None);

            Assert.Same(pointStatus, result.PointStatus);
            Assert.Equal(NodeLevel.Warning, result.OverallLevel);
            Assert.Equal(4, result.VersionStatuses.Length);
            Assert.Contains("Контроллер", result.DiagnosticReport);
        }

        [Fact]
        public async Task RefreshAsync_IgnoresProtectedNodesInDiagnosticMode()
        {
            var pointStatus = new PointStatusResult
            {
                Lm = Status(NodeLevel.Error, "ЛМ недоступен"),
                Controller = Status(NodeLevel.Ok, "Контроллер работает"),
                Esm = Status(NodeLevel.Ok, "ЕСМ работает"),
                Kkt = Status(NodeLevel.Ok, "ККТ работает"),
                Cloud = Status(NodeLevel.Ok, "Облако доступно"),
                RuDesktop = Status(NodeLevel.Ok, "RuDesktop работает")
            };
            var service = CreateService(pointStatus);

            PointStatusRefreshResult result = await service.RefreshAsync(
                null,
                null,
                includeLicensedComponents: false,
                CancellationToken.None);

            Assert.Equal(NodeLevel.Ok, result.OverallLevel);
            Assert.Empty(result.VersionStatuses);
        }

        [Fact]
        public async Task RefreshAsync_LogsEachIssueAsStructuredEvent()
        {
            var pointStatus = new PointStatusResult
            {
                Esm = Status(NodeLevel.Error, "ESM API unavailable"),
                Kkt = Status(NodeLevel.Error, "KKT unavailable"),
                Lm = Status(NodeLevel.Error, "LM unavailable"),
                Controller = Status(NodeLevel.Error, "Controller unavailable"),
                EsmApiStatus = EsmStatusResult.Unavailable(),
                EsmRegistration = EsmRegistrationResult.Unavailable(),
                CashRegister = EsmCashRegisterResult.Unavailable(),
                KktPnP = KktPnpResult.Unavailable("probe failed")
            };
            var log = new CaptureLog();
            PointStatusRefreshService service = CreateService(pointStatus, log);

            PointStatusRefreshResult result = await service.RefreshAsync(
                new IPData(), new VersionsData(), true, CancellationToken.None);

            Assert.NotEmpty(result.Diagnostics.Issues);
            Assert.Equal(result.Diagnostics.Issues.Count, log.Debug.Count);
            Assert.All(log.Debug, message => Assert.StartsWith("Event=DiagnosticIssue Code=", message));
        }

        private static PointStatusRefreshService CreateService(PointStatusResult pointStatus, ILogService log = null)
        {
            var versions = new ComponentVersionStatusService(
                new StubVersionCheckService(),
                () => "1.0");
            return new PointStatusRefreshService(
                new StubPointStatusService(pointStatus),
                versions,
                new PointStatusReportBuilder(),
                log);
        }

        private static NodeStatus Status(NodeLevel level, string text) =>
            new(level, text, text);

        private sealed class StubPointStatusService : IPointStatusService
        {
            private readonly PointStatusResult _result;

            public StubPointStatusService(PointStatusResult result) => _result = result;

            public Task<PointStatusResult> CheckAsync(CancellationToken cancellationToken) =>
                Task.FromResult(_result);
        }

        private sealed class StubVersionCheckService : IVersionCheckService
        {
            public bool NeedAtolInstall(IPData selectedIP, string expectedVersion) => false;
            public bool NeedEsmInstall(string expectedVersion) => false;
            public bool NeedControllerInstall(string expectedVersion) => false;
            public string GetAtolDriverInfo() => "1.0 (64-bit)";
            public string GetAtolDriverInfo(string requiredArchitecture) => "1.0 (64-bit)";
            public string GetEsmVersion() => "1.0";
            public string GetControllerVersion() => "1.0";
        }

        private sealed class CaptureLog : ILogService
        {
            public List<string> Debug { get; } = new();
            public void LogUser(string message, bool isError = false) { }
            public void LogDebug(string message) => Debug.Add(message);
            public string GetUserLog() => string.Empty;
        }
    }
}
