using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.PointStatus
{
    public sealed class AutoFixWorkflow
    {
        public const int DefaultMaxSteps = 6;
        private readonly AutoFixPlanner _planner;
        private readonly AutoFixExecutor _executor;
        private readonly Func<CancellationToken, Task<PointStatusRefreshResult>> _refresh;
        private readonly ILicenseOperationGuard _operationGuard;
        private readonly int _maxSteps;

        public AutoFixWorkflow(
            AutoFixPlanner planner,
            AutoFixExecutor executor,
            Func<CancellationToken, Task<PointStatusRefreshResult>> refresh,
            ILicenseOperationGuard operationGuard,
            int maxSteps = DefaultMaxSteps)
        {
            _planner = planner ?? throw new ArgumentNullException(nameof(planner));
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
            _operationGuard = operationGuard ?? throw new ArgumentNullException(nameof(operationGuard));
            _maxSteps = maxSteps > 0 ? maxSteps : throw new ArgumentOutOfRangeException(nameof(maxSteps));
        }

        public async Task<AutoFixResult> RunAsync(Action<AutoFixProgress> progress, CancellationToken cancellationToken)
        {
            _operationGuard.Demand(LicenseOperation.AutoFix);
            Report(progress, "Проверяем состояние…", Array.Empty<AutoFixStepResult>());
            AutoFixDiagnosticState initial;
            try
            {
                initial = new AutoFixDiagnosticState(await _refresh(cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Result(AutoFixStatus.Cancelled, null, new List<AutoFixStepResult>(), "Автоматическое исправление отменено.");
            }
            catch (Exception ex)
            {
                return Result(AutoFixStatus.Failed, null, new List<AutoFixStepResult>(), ex.Message);
            }

            var continuation = new AutoFixContinuation(
                initial,
                null,
                new HashSet<DiagnosticFixKey>(),
                new List<AutoFixStepResult>());
            return await ContinueSessionAsync(continuation, null, false, progress, cancellationToken).ConfigureAwait(false);
        }

        public Task<AutoFixResult> ContinueAsync(
            AutoFixContinuation continuation,
            bool confirmed,
            Action<AutoFixProgress> progress,
            CancellationToken cancellationToken)
        {
            if (continuation == null) throw new ArgumentNullException(nameof(continuation));
            _operationGuard.Demand(LicenseOperation.AutoFix);
            if (!confirmed)
                return Task.FromResult(Result(
                    AutoFixStatus.Cancelled,
                    continuation.State,
                    continuation.History,
                    "Переустановка ЛМ ЧЗ отменена пользователем."));
            return ContinueSessionAsync(continuation, continuation.PendingStep, true, progress, cancellationToken);
        }

        private async Task<AutoFixResult> ContinueSessionAsync(
            AutoFixContinuation session,
            AutoFixRepairStep approvedStep,
            bool confirmationGranted,
            Action<AutoFixProgress> progress,
            CancellationToken cancellationToken)
        {
            AutoFixDiagnosticState current = session.State;
            AutoFixRepairStep next = approvedStep;
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                    return Result(AutoFixStatus.Cancelled, current, session.History, "Автоматическое исправление отменено.");

                if (current.Snapshot.Issues.Count == 0)
                    return Result(session.History.Count == 0 ? AutoFixStatus.NoFixNeeded : AutoFixStatus.Success,
                        current, session.History, session.History.Count == 0
                            ? "Исправление не требуется."
                            : "Автоматические исправления выполнены.");

                if (session.History.Count >= _maxSteps)
                    return Result(AutoFixStatus.UnableToFix, current, session.History,
                        "Достигнут безопасный лимит автоматических исправлений.");

                next ??= _planner.BuildNext(current.Snapshot, session.AttemptedFixes);
                if (next == null)
                {
                    bool exhaustedAutomaticFix = current.Snapshot.Issues.Any(issue =>
                        issue.SuggestedFix.HasValue && session.AttemptedFixes.Contains(issue.SuggestedFix.Value));
                    return Result(
                        exhaustedAutomaticFix ? AutoFixStatus.UnableToFix : AutoFixStatus.RequiresUserAction,
                        current,
                        session.History,
                        exhaustedAutomaticFix
                            ? "Автоматическое исправление не устранило актуальную проблему."
                            : "Оставшиеся проблемы требуют действий пользователя.");
                }

                if (next.RequiresConfirmation && !confirmationGranted)
                {
                    var paused = new AutoFixContinuation(current, next, session.AttemptedFixes, session.History);
                    return new AutoFixResult(AutoFixStatus.RequiresConfirmation, current, session.History,
                        "Для переустановки ЛМ ЧЗ требуется подтверждение организации.", paused);
                }

                session.AttemptedFixes.Add(next.FixKey);
                AutoFixExecutionResult execution = await _executor.ExecuteAsync(
                    next, current, confirmationGranted,
                    message => Report(progress, message, session.History),
                    cancellationToken).ConfigureAwait(false);
                session.History.Add(new AutoFixStepResult(
                    next.FixKey, next.IssueCodes, execution.Status,
                    execution.Status == AutoFixExecutionStatus.Success
                        ? AutoFixDescriptions.CompletedFor(next.FixKey)
                        : next.Description.TrimEnd('…'),
                    execution.Error));
                Report(progress, "Проверяем результат…", session.History);

                if (execution.Status == AutoFixExecutionStatus.Cancelled)
                    return Result(AutoFixStatus.Cancelled, current, session.History, "Автоматическое исправление отменено.");
                if (execution.Status == AutoFixExecutionStatus.Unsupported)
                    return Result(AutoFixStatus.RequiresUserAction, current, session.History, execution.Error);
                if (execution.Status == AutoFixExecutionStatus.Failed)
                    return Result(AutoFixStatus.Failed, current, session.History, execution.Error);

                AutoFixDiagnosticState refreshed;
                try
                {
                    refreshed = new AutoFixDiagnosticState(await _refresh(cancellationToken).ConfigureAwait(false));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return Result(AutoFixStatus.Cancelled, current, session.History, "Автоматическое исправление отменено.");
                }
                catch (Exception ex)
                {
                    return Result(AutoFixStatus.Failed, current, session.History, ex.Message);
                }

                bool sameProblemRemains = refreshed.Snapshot.Issues.Any(issue =>
                    next.IssueCodes.Contains(issue.Code) && issue.SuggestedFix == next.FixKey);
                current = refreshed;
                if (sameProblemRemains)
                    return Result(AutoFixStatus.UnableToFix, current, session.History,
                        "После исправления проблема всё ещё обнаруживается.");

                next = null;
                confirmationGranted = false;
                session = new AutoFixContinuation(current, null, session.AttemptedFixes, session.History);
            }
        }

        private static AutoFixResult Result(
            AutoFixStatus status,
            AutoFixDiagnosticState state,
            IReadOnlyList<AutoFixStepResult> history,
            string message) => new(status, state, history.ToArray(), message);

        private static void Report(
            Action<AutoFixProgress> progress,
            string currentStep,
            IReadOnlyList<AutoFixStepResult> history) =>
            progress?.Invoke(new AutoFixProgress(currentStep, history.ToArray()));
    }
}
