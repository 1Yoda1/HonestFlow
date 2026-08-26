using System;
using System.Collections.Generic;
using System.Linq;

namespace HonestFlow.Application.PointStatus
{
    public enum AutoFixStatus
    {
        Success,
        NoFixNeeded,
        RequiresUserAction,
        RequiresConfirmation,
        UnableToFix,
        Failed,
        Cancelled
    }

    public enum AutoFixExecutionStatus
    {
        Success,
        Unsupported,
        Failed,
        Cancelled
    }

    public sealed class AutoFixDiagnosticState
    {
        public AutoFixDiagnosticState(PointStatusRefreshResult refresh)
        {
            Refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        }

        public PointStatusRefreshResult Refresh { get; }
        public PointStatusResult PointStatus => Refresh.PointStatus;
        public DiagnosticsSnapshot Snapshot => Refresh.Diagnostics;
    }

    public sealed class AutoFixRepairStep
    {
        public AutoFixRepairStep(DiagnosticFixKey fixKey, IReadOnlyList<DiagnosticIssueCode> issueCodes, bool requiresConfirmation)
        {
            FixKey = fixKey;
            IssueCodes = issueCodes ?? Array.Empty<DiagnosticIssueCode>();
            RequiresConfirmation = requiresConfirmation;
        }

        public DiagnosticFixKey FixKey { get; }
        public IReadOnlyList<DiagnosticIssueCode> IssueCodes { get; }
        public bool RequiresConfirmation { get; }
        public string Description => AutoFixDescriptions.For(FixKey);
    }

    public sealed class AutoFixStepResult
    {
        public AutoFixStepResult(
            DiagnosticFixKey fixKey,
            IReadOnlyList<DiagnosticIssueCode> issueCodes,
            AutoFixExecutionStatus executionStatus,
            string description,
            string error = null)
        {
            FixKey = fixKey;
            IssueCodes = issueCodes ?? Array.Empty<DiagnosticIssueCode>();
            ExecutionStatus = executionStatus;
            Description = description ?? string.Empty;
            Error = error;
        }

        public DiagnosticFixKey FixKey { get; }
        public IReadOnlyList<DiagnosticIssueCode> IssueCodes { get; }
        public AutoFixExecutionStatus ExecutionStatus { get; }
        public string Description { get; }
        public string Error { get; }
    }

    public sealed class AutoFixProgress
    {
        public AutoFixProgress(string currentStep, IReadOnlyList<AutoFixStepResult> history)
        {
            CurrentStep = currentStep ?? string.Empty;
            History = history ?? Array.Empty<AutoFixStepResult>();
        }

        public string CurrentStep { get; }
        public IReadOnlyList<AutoFixStepResult> History { get; }
    }

    public sealed class AutoFixExecutionResult
    {
        private AutoFixExecutionResult(AutoFixExecutionStatus status, string error)
        {
            Status = status;
            Error = error;
        }

        public AutoFixExecutionStatus Status { get; }
        public string Error { get; }
        public static AutoFixExecutionResult Success() => new(AutoFixExecutionStatus.Success, null);
        public static AutoFixExecutionResult Unsupported(string message) => new(AutoFixExecutionStatus.Unsupported, message);
        public static AutoFixExecutionResult Failed(string message) => new(AutoFixExecutionStatus.Failed, message);
        public static AutoFixExecutionResult Cancelled() => new(AutoFixExecutionStatus.Cancelled, null);
    }

    public sealed class AutoFixResult
    {
        internal AutoFixResult(
            AutoFixStatus status,
            AutoFixDiagnosticState state,
            IReadOnlyList<AutoFixStepResult> history,
            string message,
            AutoFixContinuation continuation = null)
        {
            Status = status;
            State = state;
            History = history ?? Array.Empty<AutoFixStepResult>();
            Message = message ?? string.Empty;
            Continuation = continuation;
        }

        public AutoFixStatus Status { get; }
        public AutoFixDiagnosticState State { get; }
        public DiagnosticsSnapshot Snapshot => State?.Snapshot;
        public IReadOnlyList<DiagnosticIssue> RemainingIssues => Snapshot?.Issues ?? Array.Empty<DiagnosticIssue>();
        public IReadOnlyList<AutoFixStepResult> History { get; }
        public string Message { get; }
        public AutoFixContinuation Continuation { get; }
    }

    public sealed class AutoFixContinuation
    {
        internal AutoFixContinuation(
            AutoFixDiagnosticState state,
            AutoFixRepairStep pendingStep,
            HashSet<DiagnosticFixKey> attemptedFixes,
            List<AutoFixStepResult> history)
        {
            State = state;
            PendingStep = pendingStep;
            AttemptedFixes = attemptedFixes;
            History = history;
        }

        public AutoFixRepairStep PendingStep { get; }
        internal AutoFixDiagnosticState State { get; }
        internal HashSet<DiagnosticFixKey> AttemptedFixes { get; }
        internal List<AutoFixStepResult> History { get; }
    }

    internal static class AutoFixDescriptions
    {
        public static string For(DiagnosticFixKey key) => key switch
        {
            DiagnosticFixKey.RunSmartInstallation => "Устанавливаем и обновляем необходимые компоненты…",
            DiagnosticFixKey.StartEsmServices => "Запускаем службы ТС ПИоТ…",
            DiagnosticFixKey.RestartEsm => "Перезапускаем ТС ПИоТ…",
            DiagnosticFixKey.StartKktServices => "Запускаем службы ККТ…",
            DiagnosticFixKey.RestartLm => "Восстанавливаем службы ЛМ ЧЗ…",
            DiagnosticFixKey.RestartLmController => "Перезапускаем контроллер ЛМ ЧЗ…",
            DiagnosticFixKey.InitializeLm => "Настраиваем ЛМ ЧЗ…",
            DiagnosticFixKey.RepairLmSync => "Восстанавливаем синхронизацию ЛМ ЧЗ…",
            DiagnosticFixKey.RegisterTsPiot => "Регистрируем ТС ПИоТ…",
            DiagnosticFixKey.ConfirmLmClientMismatch => "Подтверждаем организацию ЛМ ЧЗ…",
            _ => "Выполняем восстановление…"
        };

        public static string CompletedFor(DiagnosticFixKey key) => key switch
        {
            DiagnosticFixKey.RunSmartInstallation => "Установлены и обновлены необходимые компоненты",
            DiagnosticFixKey.StartEsmServices => "Запущены службы ТС ПИоТ",
            DiagnosticFixKey.RestartEsm => "Перезапущен ТС ПИоТ",
            DiagnosticFixKey.StartKktServices => "Запущены службы ККТ",
            DiagnosticFixKey.RestartLm => "Восстановлены службы ЛМ ЧЗ",
            DiagnosticFixKey.RestartLmController => "Перезапущен контроллер ЛМ ЧЗ",
            DiagnosticFixKey.InitializeLm => "Выполнена настройка ЛМ ЧЗ",
            DiagnosticFixKey.RepairLmSync => "Восстановлена синхронизация ЛМ ЧЗ",
            DiagnosticFixKey.RegisterTsPiot => "Выполнена регистрация ТС ПИоТ",
            DiagnosticFixKey.ConfirmLmClientMismatch => "Переустановлен ЛМ ЧЗ для подтверждённой организации",
            _ => "Выполнен шаг восстановления"
        };
    }
}
