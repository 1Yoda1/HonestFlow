using System;
using System.Collections.Generic;
using HonestFlow.Application.PointStatus;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class AutoFixPlannerTests
    {
        private readonly AutoFixPlanner _planner = new();

        [Fact]
        public void OneIssue_SelectsItsSuggestedFix()
        {
            AutoFixRepairStep step = _planner.BuildNext(Snapshot(
                Issue(DiagnosticIssueCode.ESM_API_UNAVAILABLE, DiagnosticFixKey.RestartEsm)));

            Assert.Equal(DiagnosticFixKey.RestartEsm, step.FixKey);
        }

        [Fact]
        public void DuplicateFixKeys_CreateOneStepWithAllIssueCodes()
        {
            AutoFixRepairStep step = _planner.BuildNext(Snapshot(
                Issue(DiagnosticIssueCode.ESM_NOT_INSTALLED, DiagnosticFixKey.RunSmartInstallation),
                Issue(DiagnosticIssueCode.ATOL_DRIVER_TOO_OLD, DiagnosticFixKey.RunSmartInstallation)));

            Assert.Equal(DiagnosticFixKey.RunSmartInstallation, step.FixKey);
            Assert.Equal(2, step.IssueCodes.Count);
        }

        [Fact]
        public void SmartInstallation_WinsOverServiceRepair()
        {
            AutoFixRepairStep step = _planner.BuildNext(Snapshot(
                Issue(DiagnosticIssueCode.ATOL_GRPC_SERVICE_STOPPED, DiagnosticFixKey.StartKktServices),
                Issue(DiagnosticIssueCode.ATOL_DRIVER_TOO_OLD, DiagnosticFixKey.RunSmartInstallation)));

            Assert.Equal(DiagnosticFixKey.RunSmartInstallation, step.FixKey);
        }

        [Fact]
        public void ServiceRepair_IsSelectedWhenStructuralRepairIsAbsent()
        {
            AutoFixRepairStep step = _planner.BuildNext(Snapshot(
                Issue(DiagnosticIssueCode.LM_NOT_CONFIGURED, DiagnosticFixKey.InitializeLm),
                Issue(DiagnosticIssueCode.ESM_API_UNAVAILABLE, DiagnosticFixKey.RestartEsm)));

            Assert.Equal(DiagnosticFixKey.RestartEsm, step.FixKey);
        }

        [Fact]
        public void UserActionOnlyIssue_DoesNotCreateRepairStep()
        {
            Assert.Null(_planner.BuildNext(Snapshot(Issue(DiagnosticIssueCode.KKT_NOT_DETECTED, null))));
        }

        [Fact]
        public void AttemptedRepair_IsNotSelectedAgain()
        {
            Assert.Null(_planner.BuildNext(
                Snapshot(Issue(DiagnosticIssueCode.ESM_API_UNAVAILABLE, DiagnosticFixKey.RestartEsm)),
                new[] { DiagnosticFixKey.RestartEsm }));
        }

        [Fact]
        public void LmMismatch_IsInteractiveRepair()
        {
            AutoFixRepairStep step = _planner.BuildNext(Snapshot(
                Issue(DiagnosticIssueCode.LM_INN_MISMATCH, DiagnosticFixKey.ConfirmLmClientMismatch)));

            Assert.True(step.RequiresConfirmation);
        }

        internal static DiagnosticIssue Issue(DiagnosticIssueCode code, DiagnosticFixKey? fix) =>
            new(code, DiagnosticComponent.Esm, DiagnosticSeverity.WorkImpossible,
                code.ToString(), "message", "details", Array.Empty<DiagnosticEvidence>(), fix);

        internal static DiagnosticsSnapshot Snapshot(params DiagnosticIssue[] issues) => new()
        {
            Issues = issues,
            WorkState = issues.Length == 0 ? WorkState.Ready : WorkState.WorkImpossible,
            ObservedAtUtc = DateTimeOffset.UtcNow
        };
    }
}
