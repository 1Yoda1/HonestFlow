using HonestFlow.Application.PointStatus;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class DiagnosticIssuePresentationMapperTests
    {
        private readonly DiagnosticIssuePresentationMapper _mapper = new();

        [Fact]
        public void EsmFailure_UsesHumanReadableIssue_AndKeepsRawDetailsSeparate()
        {
            DiagnosticsSnapshot snapshot = Snapshot(
                esm: new DiagnosticComponentFact(DiagnosticState.Failed, "GET /api/v1/status/42; lmController.code=5"),
                workState: WorkState.WorkImpossible);

            DiagnosticIssuePresentation issue = _mapper.Create(snapshot);

            Assert.Equal("ТС ПИоТ недоступен", issue.Title);
            Assert.DoesNotContain("GET", issue.Description);
            Assert.DoesNotContain("code", issue.Description);
            Assert.Contains("автоматическое восстановление", issue.Recommendation);
            Assert.Contains("lmController.code=5", issue.TechnicalDetails);
        }

        [Fact]
        public void WorkImpossible_UsesShortSimpleDescriptionWithoutRawFacts()
        {
            string description = _mapper.SimpleDescription(WorkState.WorkImpossible);

            Assert.Contains("Один из необходимых компонентов", description);
            Assert.DoesNotContain("GET", description);
            Assert.DoesNotContain("error", description);
        }

        [Fact]
        public void NodeStatuses_AreShortAndDoNotExposeRawProbeValues()
        {
            Assert.Equal("API недоступен", _mapper.ComponentStatus(
                new DiagnosticComponentFact(DiagnosticState.Failed, "LM.Data: source=probe"), "API недоступен"));
            Assert.Equal("Служба остановлена", _mapper.ComponentStatus(
                new DiagnosticComponentFact(DiagnosticState.Failed, "lastConnection=none"), "Служба остановлена"));
            Assert.Equal("Не подключена", _mapper.ConnectionStatus(
                new DiagnosticConnectionFact(DiagnosticConnectionState.Disconnected, "CashRegister.Data.kkt[] empty")));
        }

        [Fact]
        public void Attention_UsesFormalizedUserMessage_NotTechnicalReason()
        {
            DiagnosticsSnapshot snapshot = Snapshot(
                esm: new DiagnosticComponentFact(DiagnosticState.Healthy, "GET /api/v1/status/42"),
                workState: WorkState.Attention);
            snapshot = new DiagnosticsSnapshot
            {
                Esm = snapshot.Esm,
                Kkt = snapshot.Kkt,
                Gismt = snapshot.Gismt,
                Lm = snapshot.Lm,
                Controller = snapshot.Controller,
                GismtToEsm = snapshot.GismtToEsm,
                EsmToKkt = snapshot.EsmToKkt,
                EsmToController = snapshot.EsmToController,
                LmConnection = snapshot.LmConnection,
                Reasons = new[] { "GET /api/v1/status/42; clientSoftware.code missing" },
                UserMessages = new[] { "Требуется внимание." },
                WorkState = WorkState.Attention
            };

            DiagnosticIssuePresentation issue = _mapper.Create(snapshot);

            Assert.Equal("Требуется внимание.", issue.Description);
            Assert.DoesNotContain("api", issue.Description, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("clientSoftware.code", issue.TechnicalDetails);
        }

        private static DiagnosticsSnapshot Snapshot(DiagnosticComponentFact esm, WorkState workState) => new()
        {
            Esm = esm,
            Kkt = new(DiagnosticState.Healthy, string.Empty),
            Gismt = new(DiagnosticState.Healthy, string.Empty),
            Lm = new(DiagnosticState.Healthy, string.Empty),
            Controller = new(DiagnosticState.Healthy, string.Empty),
            GismtToEsm = new(DiagnosticConnectionState.Connected, string.Empty),
            EsmToKkt = new(DiagnosticConnectionState.Connected, string.Empty),
            EsmToController = new(DiagnosticConnectionState.Connected, string.Empty),
            LmConnection = new(DiagnosticConnectionState.Connected, string.Empty),
            Reasons = new[] { esm.Details },
            WorkState = workState
        };
    }
}
