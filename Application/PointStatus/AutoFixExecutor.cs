using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.PointStatus
{
    public delegate Task<bool> AutoFixAction(
        AutoFixDiagnosticState state,
        Action<string> progress,
        CancellationToken cancellationToken);

    public sealed class AutoFixExecutor
    {
        private readonly IReadOnlyDictionary<DiagnosticFixKey, AutoFixAction> _actions;

        public AutoFixExecutor(IReadOnlyDictionary<DiagnosticFixKey, AutoFixAction> actions)
        {
            _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        }

        public async Task<AutoFixExecutionResult> ExecuteAsync(
            AutoFixRepairStep step,
            AutoFixDiagnosticState state,
            bool confirmationGranted,
            Action<string> progress,
            CancellationToken cancellationToken)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (step.RequiresConfirmation && !confirmationGranted)
                return AutoFixExecutionResult.Unsupported("Требуется подтверждение пользователя.");
            if (!_actions.TryGetValue(step.FixKey, out AutoFixAction action))
                return AutoFixExecutionResult.Unsupported("Для этого исправления пока нет безопасной автоматической операции.");

            try
            {
                progress?.Invoke(step.Description);
                return await action(state, progress, cancellationToken).ConfigureAwait(false)
                    ? AutoFixExecutionResult.Success()
                    : AutoFixExecutionResult.Failed("Операция восстановления не была завершена.");
            }
            catch (OperationCanceledException)
            {
                return AutoFixExecutionResult.Cancelled();
            }
            catch (Exception ex)
            {
                return AutoFixExecutionResult.Failed(ex.Message);
            }
        }
    }
}
