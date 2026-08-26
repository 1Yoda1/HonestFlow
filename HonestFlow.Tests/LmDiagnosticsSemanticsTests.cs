using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.PointStatus;
using HonestFlow.Application.RemoteAccess;
using HonestFlow.Models;
using Xunit;

namespace HonestFlow.Tests;

public sealed class LmDiagnosticsSemanticsTests
{
    [Fact]
    public async Task ReadyLm_WithMatchingCurrentClientInn_IsAvailableWithoutInnIssue()
    {
        PointStatusResult result = await CheckAsync(" 007707083893 ", "007707083893");
        DiagnosticsSnapshot snapshot = Build(result);

        Assert.Equal(LmDiagnosticProbeState.Available, result.LmProbe.State);
        Assert.True(result.LmProbe.HealthAvailable);
        Assert.Equal(LmInnComparisonState.Match, result.LmProbe.InnComparison);
        Assert.DoesNotContain(snapshot.Issues, IsInnIssue);
        Assert.Equal(DiagnosticFactState.Success,
            Assert.Single(snapshot.Facts, item => item.Key == "LmInnComparison").State);
    }

    [Fact]
    public async Task ReadyLm_WithDifferentCurrentClientInn_IsHealthyAttentionWithoutRestart()
    {
        PointStatusResult result = await CheckAsync("7707083893", "7812345678");
        DiagnosticsSnapshot snapshot = Build(result);

        Assert.Equal(LmDiagnosticProbeState.Available, result.LmProbe.State);
        Assert.True(result.LmProbe.HealthAvailable);
        Assert.Equal(DiagnosticState.Healthy, snapshot.Lm.State);
        DiagnosticIssue issue = Assert.Single(snapshot.Issues, item => item.Code == DiagnosticIssueCode.LM_INN_MISMATCH);
        Assert.Equal(DiagnosticSeverity.Attention, issue.Severity);
        Assert.Equal("ИНН ЛМ ЧЗ не соответствует клиенту", issue.Title);
        Assert.Equal("ЛМ ЧЗ настроен на другой ИНН.", issue.UserMessage);
        Assert.Equal(DiagnosticFixKey.ConfirmLmClientMismatch, issue.SuggestedFix);
        Assert.Contains(issue.Evidence, item => item.Key == "actualLmInn" && item.Value == "7707****93");
        Assert.Contains(issue.Evidence, item => item.Key == "expectedClientInn" && item.Value == "7812****78");
        Assert.DoesNotContain("7707083893", string.Join(";", issue.Evidence.Select(item => item.Value)));
        Assert.DoesNotContain("7812345678", string.Join(";", issue.Evidence.Select(item => item.Value)));
        Assert.DoesNotContain(snapshot.Issues, item => item.Code == DiagnosticIssueCode.LM_API_UNAVAILABLE);
        Assert.True(snapshot.LmPathAvailable);
        Assert.Equal(WorkState.Attention, snapshot.WorkState);
    }

    [Fact]
    public async Task ReadyLm_WithoutActualInn_StaysAvailableWithoutUserIssue()
    {
        PointStatusResult result = await CheckAsync(null, "7707083893");
        DiagnosticsSnapshot snapshot = Build(result);

        Assert.Equal(LmDiagnosticProbeState.Available, result.LmProbe.State);
        Assert.True(result.LmProbe.HealthAvailable);
        Assert.DoesNotContain(snapshot.Issues, IsInnIssue);
        Assert.DoesNotContain(snapshot.Issues, item => item.Code == DiagnosticIssueCode.LM_API_UNAVAILABLE);
    }

    [Fact]
    public async Task ReadyLm_WithoutExpectedCurrentClientInn_HasUnknownFactAndNoMismatch()
    {
        PointStatusResult result = await CheckAsync("7707083893", null);
        DiagnosticsSnapshot snapshot = Build(result);

        Assert.Equal(LmDiagnosticProbeState.Available, result.LmProbe.State);
        Assert.True(result.LmProbe.HealthAvailable);
        Assert.Equal(LmInnComparisonState.Unknown, result.LmProbe.InnComparison);
        Assert.DoesNotContain(snapshot.Issues, IsInnIssue);
        DiagnosticFact fact = Assert.Single(snapshot.Facts, item => item.Key == "LmInnComparison");
        Assert.Equal(DiagnosticFactState.Unknown, fact.State);
        Assert.Contains("Expected client INN unavailable", fact.Evidence);
    }

    [Fact]
    public async Task ExpectedInn_DoesNotFallBackToGeneralClientCollection()
    {
        PointStatusResult result = await CheckAsync(
            "7707083893",
            null,
            clients: new[] { new IPData { ClientId = "current", Inn = "7707083893" } });
        DiagnosticsSnapshot snapshot = Build(result);

        Assert.Equal(LmInnComparisonState.Unknown, result.LmProbe.InnComparison);
        Assert.DoesNotContain(snapshot.Issues, IsInnIssue);
    }

    [Fact]
    public async Task UnavailableLmApi_CreatesApiUnavailableIssue()
    {
        PointStatusResult result = await CheckAsync("7707083893", "7707083893", apiAvailable: false);
        DiagnosticsSnapshot snapshot = Build(result);

        Assert.Equal(LmDiagnosticProbeState.ApiUnavailable, result.LmProbe.State);
        Assert.False(result.LmProbe.HealthAvailable);
        Assert.Contains(snapshot.Issues, item => item.Code == DiagnosticIssueCode.LM_API_UNAVAILABLE);
    }

    [Fact]
    public async Task ReadyRuntime_CannotProduceApiUnavailableIssue()
    {
        PointStatusResult result = await CheckAsync("7707083893", "7812345678");
        DiagnosticsSnapshot snapshot = Build(result);

        Assert.Equal("ready", result.LmProbe.RuntimeStatus);
        Assert.DoesNotContain(snapshot.Issues, item => item.Code == DiagnosticIssueCode.LM_API_UNAVAILABLE);
    }

    [Fact]
    public async Task UnknownRuntime_UsesUnknownStatusIssueInsteadOfApiUnavailable()
    {
        PointStatusResult result = await CheckAsync("7707083893", "7707083893", runtimeStatus: "failure");
        DiagnosticsSnapshot snapshot = Build(result);

        Assert.Equal(LmDiagnosticProbeState.Failure, result.LmProbe.State);
        DiagnosticIssue issue = Assert.Single(snapshot.Issues, item => item.Code == DiagnosticIssueCode.LM_STATUS_UNKNOWN);
        Assert.Equal(DiagnosticSeverity.UnableToVerify, issue.Severity);
        Assert.Null(issue.SuggestedFix);
        Assert.DoesNotContain(snapshot.Issues, item => item.Code == DiagnosticIssueCode.LM_API_UNAVAILABLE);
    }

    private static bool IsInnIssue(DiagnosticIssue issue) =>
        issue.Code is DiagnosticIssueCode.LM_INN_MISMATCH;

    private static DiagnosticsSnapshot Build(PointStatusResult result)
    {
        result.AtolDriverVersion = "10.10.8.23 (64-bit)";
        return new DiagnosticsSnapshotBuilder().Create(result);
    }

    private static async Task<PointStatusResult> CheckAsync(
        string actualInn,
        string expectedInn,
        bool apiAvailable = true,
        string runtimeStatus = "ready",
        IPData[] clients = null)
    {
        var service = new PointStatusService(
            remoteConfigLoaded: true,
            ipCount: 1,
            clients: clients ?? Array.Empty<IPData>(),
            ruDesktopService: new StubRuDesktop(),
            esmStatusClient: new StubEsm(),
            serviceSnapshotProvider: new StubServices(),
            lmStatusClient: new StubLm(actualInn, apiAvailable, runtimeStatus),
            cloudConnectivityProbe: new StubCloud(),
            kktPnpProbe: new StubKktPnp(),
            kktDriverProbe: new StubKktDriver(),
            kktPortProbe: new StubKktPort(),
            esmApiPortProbe: new StubEsmApiPort());

        return await service.CheckAsync(
            new IPData { ClientId = "current", Name = "Текущий клиент", Inn = expectedInn },
            CancellationToken.None);
    }

    private sealed class StubLm : ILmStatusClient
    {
        private readonly string _inn;
        private readonly bool _available;
        private readonly string _runtimeStatus;
        public StubLm(string inn, bool available, string runtimeStatus)
        {
            _inn = inn;
            _available = available;
            _runtimeStatus = runtimeStatus;
        }
        public Task<ApiResponse<LmStatus>> GetStatus() => Task.FromResult(_available
            ? ApiResponse<LmStatus>.Success(new LmStatus { Status = _runtimeStatus, Inn = _inn, Version = "2.5.1" }, HttpStatusCode.OK)
            : ApiResponse<LmStatus>.Failure(HttpStatusCode.ServiceUnavailable, "connection refused"));
    }

    private sealed class StubEsm : IEsmStatusClient
    {
        public Task<EsmStatusResult> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(EsmStatusResult.Success(new EsmStatusDto
            {
                Gismt = Code(0),
                LmController = Code(0),
                Lm = Code(0),
                LmInfo = new EsmLmInfoDto { Code = 0, LmStatus = new EsmLmStatusDto { Status = "ready" } }
            }));
        public Task<EsmCashRegisterResult> GetCashRegisterStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(EsmCashRegisterResult.Connected());
        public Task<EsmRegistrationResult> GetRegistrationStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(EsmRegistrationResult.Registered());
        private static EsmComponentStatus Code(int code) => new() { Code = code };
    }

    private sealed class StubServices : IWindowsServiceSnapshotProvider
    {
        public Task<ServiceSnapshot[]> GetSnapshotsAsync(CancellationToken cancellationToken) => Task.FromResult(new[]
        {
            new ServiceSnapshot("esm-orchestrator", "Running"),
            new ServiceSnapshot("esm-cm-store", "Running"),
            new ServiceSnapshot("uem-agent", "Running"),
            new ServiceSnapshot("uem-updater", "Running"),
            new ServiceSnapshot("atol-grpc-service", "Running"),
            new ServiceSnapshot("regime", "Running"),
            new ServiceSnapshot("yenisei", "Running")
        });
    }

    private sealed class StubRuDesktop : IRuDesktopStatusProvider
    {
        public Task<RuDesktopStatus> GetStatus() => Task.FromResult(new RuDesktopStatus
        {
            IsInstalled = true,
            ServiceInstalled = true,
            ServiceRunning = true,
            InstallationState = RuDesktopInstallationState.Ready,
            Id = "123456"
        });
    }

    private sealed class StubCloud : ICloudConnectivityProbe
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class StubEsmApiPort : IEsmApiPortProbe
    {
        public Task<EsmApiPortProbeResult> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(EsmApiPortProbeResult.Available(51077));
    }

    private sealed class StubKktPnp : IKktPnpProbe
    {
        public Task<KktPnpResult> DetectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(KktPnpResult.Detected("ATOL 30F"));
    }

    private sealed class StubKktDriver : IKktDriverProbe
    {
        public Task<KktDriverProbeResult> CheckAsync(string requiredArchitecture, CancellationToken cancellationToken) =>
            Task.FromResult(KktDriverProbeResult.Found(requiredArchitecture, "10.10.8.23"));
    }

    private sealed class StubKktPort : IKktPortProbe
    {
        public Task<KktPortProbeResult> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(KktPortProbeResult.Available());
    }
}
