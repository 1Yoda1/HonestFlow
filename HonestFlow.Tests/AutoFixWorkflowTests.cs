using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.PointStatus;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class AutoFixWorkflowTests
    {
        [Fact]
        public async Task OneRepairThenHealthy_ReturnsSuccessAndStartsWithFreshDiagnostics()
        {
            int repairs = 0;
            var progress = new List<AutoFixProgress>();
            var refresh = RefreshSequence(
                AutoFixPlannerTests.Snapshot(Issue(DiagnosticIssueCode.ESM_API_UNAVAILABLE, DiagnosticFixKey.RestartEsm)),
                AutoFixPlannerTests.Snapshot());
            AutoFixWorkflow workflow = Workflow(refresh.Provider,
                Action(DiagnosticFixKey.RestartEsm, () => repairs++));

            AutoFixResult result = await workflow.RunAsync(progress.Add, CancellationToken.None);

            Assert.Equal(AutoFixStatus.Success, result.Status);
            Assert.Equal(1, repairs);
            Assert.Equal(2, refresh.Calls);
            Assert.Contains(progress, item => item.CurrentStep == "Проверяем состояние…");
            Assert.Contains(progress, item => item.History.Count == 1 &&
                item.History[0].Description == "Перезапущен ТС ПИоТ");
        }

        [Fact]
        public async Task NewIssueAfterRepair_IsReplannedAndRepairedSequentially()
        {
            var order = new List<DiagnosticFixKey>();
            var refresh = RefreshSequence(
                Snapshot(DiagnosticIssueCode.ESM_API_UNAVAILABLE, DiagnosticFixKey.RestartEsm),
                Snapshot(DiagnosticIssueCode.LM_SYNC_ERROR, DiagnosticFixKey.RepairLmSync),
                AutoFixPlannerTests.Snapshot());
            AutoFixWorkflow workflow = Workflow(refresh.Provider,
                Action(DiagnosticFixKey.RestartEsm, () => order.Add(DiagnosticFixKey.RestartEsm)),
                Action(DiagnosticFixKey.RepairLmSync, () => order.Add(DiagnosticFixKey.RepairLmSync)));

            AutoFixResult result = await workflow.RunAsync(null, CancellationToken.None);

            Assert.Equal(AutoFixStatus.Success, result.Status);
            Assert.Equal(new[] { DiagnosticFixKey.RestartEsm, DiagnosticFixKey.RepairLmSync }, order);
        }

        [Fact]
        public async Task SeveralIssuesWithSmartInstallation_InvokeInstallerOnce()
        {
            int installs = 0;
            var refresh = RefreshSequence(
                AutoFixPlannerTests.Snapshot(
                    Issue(DiagnosticIssueCode.ESM_NOT_INSTALLED, DiagnosticFixKey.RunSmartInstallation),
                    Issue(DiagnosticIssueCode.ATOL_DRIVER_TOO_OLD, DiagnosticFixKey.RunSmartInstallation)),
                AutoFixPlannerTests.Snapshot());
            AutoFixWorkflow workflow = Workflow(refresh.Provider,
                Action(DiagnosticFixKey.RunSmartInstallation, () => installs++));

            AutoFixResult result = await workflow.RunAsync(null, CancellationToken.None);

            Assert.Equal(AutoFixStatus.Success, result.Status);
            Assert.Equal(1, installs);
            Assert.Single(result.History);
            Assert.Equal(2, result.History[0].IssueCodes.Count);
        }

        [Fact]
        public async Task SameIssueAfterRepair_DoesNotRepeatAndReturnsUnableToFix()
        {
            int repairs = 0;
            DiagnosticsSnapshot broken = Snapshot(DiagnosticIssueCode.ESM_API_UNAVAILABLE, DiagnosticFixKey.RestartEsm);
            var refresh = RefreshSequence(broken, broken);
            AutoFixWorkflow workflow = Workflow(refresh.Provider,
                Action(DiagnosticFixKey.RestartEsm, () => repairs++));

            AutoFixResult result = await workflow.RunAsync(null, CancellationToken.None);

            Assert.Equal(AutoFixStatus.UnableToFix, result.Status);
            Assert.Equal(1, repairs);
        }

        [Fact]
        public async Task UserActionOnlyIssue_ReturnsRequiresUserActionWithoutExecution()
        {
            int repairs = 0;
            var refresh = RefreshSequence(Snapshot(DiagnosticIssueCode.KKT_NOT_DETECTED, null));
            AutoFixWorkflow workflow = Workflow(refresh.Provider,
                Action(DiagnosticFixKey.RestartEsm, () => repairs++));

            AutoFixResult result = await workflow.RunAsync(null, CancellationToken.None);

            Assert.Equal(AutoFixStatus.RequiresUserAction, result.Status);
            Assert.Equal(0, repairs);
        }

        [Fact]
        public async Task LmMismatch_PausesUntilConfirmationThenContinues()
        {
            int reinstalls = 0;
            var refresh = RefreshSequence(
                Snapshot(DiagnosticIssueCode.LM_INN_MISMATCH, DiagnosticFixKey.ConfirmLmClientMismatch),
                AutoFixPlannerTests.Snapshot());
            AutoFixWorkflow workflow = Workflow(refresh.Provider,
                Action(DiagnosticFixKey.ConfirmLmClientMismatch, () => reinstalls++));

            AutoFixResult paused = await workflow.RunAsync(null, CancellationToken.None);
            Assert.Equal(AutoFixStatus.RequiresConfirmation, paused.Status);
            Assert.Equal(0, reinstalls);

            AutoFixResult completed = await workflow.ContinueAsync(
                paused.Continuation, true, null, CancellationToken.None);

            Assert.Equal(AutoFixStatus.Success, completed.Status);
            Assert.Equal(1, reinstalls);
        }

        [Fact]
        public async Task LmMismatch_DeclinedConfirmationCancelsWithoutReinstall()
        {
            int reinstalls = 0;
            var refresh = RefreshSequence(
                Snapshot(DiagnosticIssueCode.LM_INN_MISMATCH, DiagnosticFixKey.ConfirmLmClientMismatch));
            AutoFixWorkflow workflow = Workflow(refresh.Provider,
                Action(DiagnosticFixKey.ConfirmLmClientMismatch, () => reinstalls++));

            AutoFixResult paused = await workflow.RunAsync(null, CancellationToken.None);
            AutoFixResult cancelled = await workflow.ContinueAsync(
                paused.Continuation, false, null, CancellationToken.None);

            Assert.Equal(AutoFixStatus.Cancelled, cancelled.Status);
            Assert.Equal(0, reinstalls);
        }

        [Fact]
        public async Task MaxSteps_StopsBeforeExecutingAnotherRepair()
        {
            int secondRepair = 0;
            var refresh = RefreshSequence(
                Snapshot(DiagnosticIssueCode.ESM_API_UNAVAILABLE, DiagnosticFixKey.RestartEsm),
                Snapshot(DiagnosticIssueCode.LM_SYNC_ERROR, DiagnosticFixKey.RepairLmSync));
            AutoFixWorkflow workflow = Workflow(refresh.Provider, 1,
                Action(DiagnosticFixKey.RestartEsm, () => { }),
                Action(DiagnosticFixKey.RepairLmSync, () => secondRepair++));

            AutoFixResult result = await workflow.RunAsync(null, CancellationToken.None);

            Assert.Equal(AutoFixStatus.UnableToFix, result.Status);
            Assert.Equal(0, secondRepair);
        }

        private static AutoFixWorkflow Workflow(
            Func<CancellationToken, Task<PointStatusRefreshResult>> refresh,
            params KeyValuePair<DiagnosticFixKey, AutoFixAction>[] actions) => Workflow(refresh, 6, actions);

        private static AutoFixWorkflow Workflow(
            Func<CancellationToken, Task<PointStatusRefreshResult>> refresh,
            int maxSteps,
            params KeyValuePair<DiagnosticFixKey, AutoFixAction>[] actions) =>
            new(new AutoFixPlanner(), new AutoFixExecutor(new Dictionary<DiagnosticFixKey, AutoFixAction>(actions)), refresh, maxSteps);

        private static KeyValuePair<DiagnosticFixKey, AutoFixAction> Action(DiagnosticFixKey key, Action callback) =>
            new(key, (_, _, _) =>
            {
                callback();
                return Task.FromResult(true);
            });

        private static DiagnosticsSnapshot Snapshot(DiagnosticIssueCode code, DiagnosticFixKey? fix) =>
            AutoFixPlannerTests.Snapshot(Issue(code, fix));

        private static DiagnosticIssue Issue(DiagnosticIssueCode code, DiagnosticFixKey? fix) =>
            AutoFixPlannerTests.Issue(code, fix);

        private static RefreshStub RefreshSequence(params DiagnosticsSnapshot[] snapshots) => new(snapshots);

        private sealed class RefreshStub
        {
            private readonly Queue<DiagnosticsSnapshot> _snapshots;
            public RefreshStub(IEnumerable<DiagnosticsSnapshot> snapshots) => _snapshots = new Queue<DiagnosticsSnapshot>(snapshots);
            public int Calls { get; private set; }
            public Task<PointStatusRefreshResult> Provider(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls++;
                DiagnosticsSnapshot snapshot = _snapshots.Dequeue();
                return Task.FromResult(new PointStatusRefreshResult(
                    new PointStatusResult(), Array.Empty<HonestFlow.Application.Installation.ComponentVersionStatus>(),
                    string.Empty, NodeLevel.Ok, snapshot));
            }
        }
    }
}
