using System;
using System.Collections.Generic;
using System.Linq;

namespace HonestFlow.Application.PointStatus
{
    public sealed class AutoFixPlanner
    {
        private static readonly IReadOnlyDictionary<DiagnosticFixKey, int> Priorities =
            new Dictionary<DiagnosticFixKey, int>
            {
                [DiagnosticFixKey.RunSmartInstallation] = 0,
                [DiagnosticFixKey.StartEsmServices] = 10,
                [DiagnosticFixKey.RestartEsm] = 11,
                [DiagnosticFixKey.StartKktServices] = 12,
                [DiagnosticFixKey.RestartLm] = 13,
                [DiagnosticFixKey.RestartLmController] = 14,
                [DiagnosticFixKey.InitializeLm] = 20,
                [DiagnosticFixKey.RepairLmSync] = 21,
                [DiagnosticFixKey.RegisterTsPiot] = 22,
                [DiagnosticFixKey.ConfirmLmClientMismatch] = 30
            };

        public AutoFixRepairStep BuildNext(
            DiagnosticsSnapshot snapshot,
            IReadOnlyCollection<DiagnosticFixKey> attemptedFixes = null)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var attempted = attemptedFixes == null
                ? new HashSet<DiagnosticFixKey>()
                : new HashSet<DiagnosticFixKey>(attemptedFixes);

            IGrouping<DiagnosticFixKey, DiagnosticIssue> group = snapshot.Issues
                .Where(issue => issue.SuggestedFix.HasValue && !attempted.Contains(issue.SuggestedFix.Value))
                .GroupBy(issue => issue.SuggestedFix.Value)
                .OrderBy(item => Priority(item.Key))
                .ThenBy(item => item.Key)
                .FirstOrDefault();

            return group == null
                ? null
                : new AutoFixRepairStep(
                    group.Key,
                    group.Select(issue => issue.Code).Distinct().ToArray(),
                    group.Key == DiagnosticFixKey.ConfirmLmClientMismatch);
        }

        private static int Priority(DiagnosticFixKey key) =>
            Priorities.TryGetValue(key, out int priority) ? priority : int.MaxValue;
    }
}
