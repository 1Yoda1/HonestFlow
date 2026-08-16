using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using HonestFlow.Application.PointStatus;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class DiagnosticIssueAndDebugModeTests
    {
        private readonly DiagnosticsSnapshotBuilder _builder = new();

        [Fact]
        public void EsmApiUnavailable_HasStableIssueCodeAndStructuredFields()
        {
            PointStatusResult result = Healthy();
            result.EsmApiStatus = EsmStatusResult.Unavailable();

            DiagnosticIssue issue = _builder.Create(result).Issues
                .Single(item => item.Code == DiagnosticIssueCode.ESM_API_UNAVAILABLE);

            Assert.Equal(DiagnosticSeverity.WorkImpossible, issue.Severity);
            Assert.Equal(DiagnosticComponent.Esm, issue.Component);
            Assert.Equal(DiagnosticFixKey.RestartEsm, issue.SuggestedFix);
            Assert.Contains("API", issue.UserMessage);
            Assert.NotEqual(issue.UserMessage, issue.TechnicalDetails);
            Assert.Contains("Event=DiagnosticIssue Code=ESM_API_UNAVAILABLE", issue.ToStructuredLog());
        }

        [Fact]
        public void StoppedAtolGrpc_HasCorrectIssueAndSeverity()
        {
            PointStatusResult result = Healthy();
            result.Kkt = Node(NodeLevel.Error, new ServiceSnapshot("atol-grpc-service", "Stopped"));
            result.ServiceSnapshots = result.ServiceSnapshots
                .Where(service => !service.ServiceName.Equals("atol-grpc-service", StringComparison.OrdinalIgnoreCase))
                .Append(new ServiceSnapshot("atol-grpc-service", "Stopped"))
                .ToArray();

            DiagnosticIssue issue = _builder.Create(result).Issues
                .Single(item => item.Code == DiagnosticIssueCode.ATOL_GRPC_SERVICE_STOPPED);

            Assert.Equal(DiagnosticSeverity.WorkImpossible, issue.Severity);
            Assert.Equal(DiagnosticFixKey.StartAtolGrpcService, issue.SuggestedFix);
        }

        [Fact]
        public void LmSyncError_WithHealthyGis_IsAttention()
        {
            PointStatusResult result = Healthy();
            result.Lm = Node(NodeLevel.Error, new ServiceSnapshot("regime", "Running"));
            result.LmProbe = new LmDiagnosticProbeResult(result.Lm, false, LmDiagnosticProbeState.SyncError, "sync_error");

            DiagnosticIssue issue = _builder.Create(result).Issues
                .Single(item => item.Code == DiagnosticIssueCode.LM_SYNC_ERROR);

            Assert.Equal(DiagnosticSeverity.Attention, issue.Severity);
            Assert.Equal(DiagnosticFixKey.RepairLmSync, issue.SuggestedFix);
        }

        [Fact]
        public void GisAndLmPathDown_HasMarkingChannelUnavailableIssue()
        {
            PointStatusResult result = Healthy();
            result.EsmApiStatus.Status.Gismt.Code = 4;
            result.EsmApiStatus.Status.Lm.Code = 5;

            DiagnosticsSnapshot snapshot = _builder.Create(result);

            Assert.Equal(WorkState.WorkImpossible, snapshot.WorkState);
            Assert.Contains(snapshot.Issues, issue => issue.Code == DiagnosticIssueCode.MARKING_CHANNEL_UNAVAILABLE);
        }

        [Fact]
        public void ClientSoftware_DoesNotCreateIssueOrAffectReadyState()
        {
            PointStatusResult result = Healthy();
            result.EsmApiStatus.Status.ClientSoftware = null;

            DiagnosticsSnapshot snapshot = _builder.Create(result);

            Assert.Equal(WorkState.Ready, snapshot.WorkState);
            Assert.DoesNotContain(snapshot.Issues, issue => issue.Component == DiagnosticComponent.ClientSoftware);
        }

        [Fact]
        public void Snapshot_ContainsStructuredFactsIssuesAndTimestamp()
        {
            DiagnosticsSnapshot snapshot = _builder.Create(Healthy());

            Assert.NotEqual(default, snapshot.ObservedAtUtc);
            Assert.Contains(snapshot.Facts, fact => fact.Key == "Esm.Api");
            Assert.Contains(snapshot.Facts, fact => fact.Key == "Kkt.PnP");
            Assert.Contains(snapshot.Facts, fact => fact.Key == "Lm.Health");
            Assert.Contains(snapshot.Facts, fact => fact.Key == "GisMt");
        }

        [Fact]
        public void ClientSoftwareMetadata_RemainsAvailableInDeveloperFacts()
        {
            PointStatusResult result = Healthy();
            result.EsmApiStatus.Status.ClientSoftware = new EsmComponentStatus
            {
                Name = "МойСклад-ПОС",
                Version = "4.344",
                Id = "pos-17",
                Code = 8,
                LastConnection = "earlier"
            };
            DiagnosticsSnapshot snapshot = _builder.Create(result);

            DiagnosticFact fact = Assert.Single(snapshot.Facts, item => item.Key == "ClientSoftware");
            Assert.Equal(DiagnosticFactState.Unknown, fact.State);
            Assert.Equal("МойСклад-ПОС", fact.Value);
            Assert.Contains("version=4.344", fact.Evidence);
            Assert.Contains("lastConnection=earlier", fact.Evidence);
            Assert.Equal(WorkState.Ready, snapshot.WorkState);
        }

        [Fact]
        public void DebugMode_IsEnabledByDefault_AndKeepsCommandLineAndEnvironmentSupport()
        {
            Assert.True(DiagnosticsDebugMode.IsEnabled(Array.Empty<string>(), environmentValue: "0"));
            Assert.True(DiagnosticsDebugMode.IsEnabled(new[] { "HonestFlow.exe", "--diagnostics-debug" }, environmentValue: "0"));
            Assert.True(DiagnosticsDebugMode.IsEnabled(Array.Empty<string>(), environmentValue: "true"));
        }

        [Fact]
        public async Task DeveloperSession_UsesInitialSnapshotAndRefreshDelegateResult()
        {
            DiagnosticsSnapshot initial = _builder.Create(Healthy());
            PointStatusResult changed = Healthy();
            changed.EsmApiStatus.Status.Gismt.Code = 8;
            DiagnosticsSnapshot refreshed = _builder.Create(changed);
            int calls = 0;
            var session = new DeveloperDiagnosticsSession(initial, _ =>
            {
                calls++;
                return Task.FromResult(refreshed);
            });

            Assert.Same(initial, session.Snapshot);
            DiagnosticsSnapshot actual = await session.RefreshAsync(CancellationToken.None);

            Assert.Same(refreshed, actual);
            Assert.Same(refreshed, session.Snapshot);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void DeveloperUi_IsVisibleByDefault_AndExposesStructuredSnapshotColumns()
        {
            XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
            XDocument main = XDocument.Load(ProjectFile("HonestFlow.WpfPrototype", "MainWindow.xaml"));
            XElement button = main.Descendants(presentation + "Button")
                .Single(element => (string)element.Attribute(xaml + "Name") == "DeveloperDiagnosticsButton");
            Assert.Equal("Visible", (string)button.Attribute("Visibility"));
            Assert.DoesNotContain("TechnicalDetails", main.ToString());
            Assert.DoesNotContain("Товароучётная", main.ToString());
            Assert.DoesNotContain("Accounting", main.ToString());

            XDocument debug = XDocument.Load(ProjectFile("HonestFlow.WpfPrototype", "DeveloperDiagnosticsWindow.xaml"));
            string[] headers = debug.Descendants(presentation + "DataGridTextColumn")
                .Select(column => (string)column.Attribute("Header"))
                .ToArray();
            Assert.Contains("Code", headers);
            Assert.Contains("TechnicalDetails", headers);
            Assert.Contains("Evidence", headers);

            string mainCode = File.ReadAllText(ProjectFile("HonestFlow.WpfPrototype", "MainWindow.xaml.cs"));
            Assert.Contains("new DeveloperDiagnosticsSession(_lastDiagnostics", mainCode);
            Assert.Contains("_pointStatusRefresh.RefreshAsync", mainCode);
        }

        private static PointStatusResult Healthy()
        {
            ServiceSnapshot[] services =
            {
                new("esm-orchestrator", "Running"), new("esm-cm-store", "Running"),
                new("atol-grpc-service", "Running"), new("regime", "Running"),
                new("yenisei", "Running"), new("esm-lm-controller", "Running")
            };
            NodeStatus lm = Node(NodeLevel.Ok, services[3], services[4]);
            return new PointStatusResult
            {
                Esm = Node(NodeLevel.Ok, services[0], services[1]),
                Kkt = Node(NodeLevel.Ok, services[2]),
                Lm = lm,
                Controller = Node(NodeLevel.Ok, services[5]),
                EsmRegistration = EsmRegistrationResult.Registered(),
                CashRegister = EsmCashRegisterResult.Connected(),
                KktPnP = KktPnpResult.NotDetected(),
                AtolDriverVersion = "10.10.8.23 (64-bit)",
                ServiceSnapshots = services,
                LmProbe = new LmDiagnosticProbeResult(lm, true, LmDiagnosticProbeState.Available, "ready"),
                EsmApiStatus = EsmStatusResult.Success(new EsmStatusDto
                {
                    ClientSoftware = Code(0), Gismt = Code(0), LmController = Code(0), Lm = Code(0)
                })
            };
        }

        private static EsmComponentStatus Code(int code) => new() { Code = code, Name = "component" };
        private static NodeStatus Node(NodeLevel level, params ServiceSnapshot[] services) =>
            new(level, level.ToString(), string.Join(";", services.Select(service => service.ServiceName + "=" + service.State)), services);
        private static string ProjectFile(params string[] parts) => Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(parts).ToArray()));
    }
}
