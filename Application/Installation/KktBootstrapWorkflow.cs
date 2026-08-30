using System;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Core;
using HonestFlow.Application.PointStatus;
using HonestFlow.Models;

namespace HonestFlow.Application.Installation
{
    public sealed class KktBootstrapWorkflow
    {
        private readonly IKktBootstrapProcessClient _processClient;
        private readonly IEsmStatusClient _esmStatusClient;
        private readonly TsPiotRegistrationWorkflow _registrationWorkflow;
        private readonly Func<CancellationToken, Task> _restartEsm;
        private readonly Func<CancellationToken, Task<DiagnosticsSnapshot>> _refreshDiagnostics;
        private readonly Func<CancellationToken, Task<bool>> _confirmGisMtWait;
        private readonly IProgressService _progress;
        private readonly KktBootstrapWorkflowOptions _options;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        public KktBootstrapWorkflow(
            IKktBootstrapProcessClient processClient,
            IEsmStatusClient esmStatusClient,
            TsPiotRegistrationWorkflow registrationWorkflow,
            Func<CancellationToken, Task> restartEsm,
            Func<CancellationToken, Task<DiagnosticsSnapshot>> refreshDiagnostics,
            Func<CancellationToken, Task<bool>> confirmGisMtWait,
            IProgressService progress,
            KktBootstrapWorkflowOptions options = null,
            Func<DateTimeOffset> utcNow = null,
            Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            _processClient = processClient ?? throw new ArgumentNullException(nameof(processClient));
            _esmStatusClient = esmStatusClient ?? throw new ArgumentNullException(nameof(esmStatusClient));
            _registrationWorkflow = registrationWorkflow ?? throw new ArgumentNullException(nameof(registrationWorkflow));
            _restartEsm = restartEsm ?? throw new ArgumentNullException(nameof(restartEsm));
            _refreshDiagnostics = refreshDiagnostics ?? throw new ArgumentNullException(nameof(refreshDiagnostics));
            _confirmGisMtWait = confirmGisMtWait ?? throw new ArgumentNullException(nameof(confirmGisMtWait));
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            _options = options ?? KktBootstrapWorkflowOptions.Default;
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            _delay = delay ?? Task.Delay;
        }

        public async Task<KktBootstrapResult> RunAsync(IPData selectedClient, CancellationToken cancellationToken)
        {
            if (selectedClient == null) throw new ArgumentNullException(nameof(selectedClient));
            string architecture;
            try { architecture = NormalizeArchitecture(selectedClient.Architecture); }
            catch (InvalidOperationException ex)
            {
                return KktBootstrapResult.Create(KktBootstrapStatus.HelperFailed, ex.Message);
            }
            string requiredVersion = selectedClient.Versions?.AtolDriver?.Trim();
            if (string.IsNullOrWhiteSpace(requiredVersion))
                return KktBootstrapResult.Create(KktBootstrapStatus.HelperFailed,
                    "Сервер не передал требуемую версию драйвера АТОЛ.");

            IKktBootstrapSession session = null;
            try
            {
                _progress.SetProgress(72, "Проверяем драйвер АТОЛ...");
                KktBootstrapStartResult start = await _processClient
                    .StartAsync(architecture, requiredVersion, cancellationToken)
                    .ConfigureAwait(false);
                if (start.Status != KktBootstrapStartStatus.Connected)
                    return StartFailure(start);

                session = start.Session;
                _progress.SetProgress(76, "ККТ подключена. Ожидаем обнаружение ККТ в ТС ПИоТ...");
                if (!await WaitForKktAsync(cancellationToken).ConfigureAwait(false))
                {
                    _progress.SetProgress(79, "ККТ пока не обнаружена. Перезапускаем ESM...");
                    await _restartEsm(cancellationToken).ConfigureAwait(false);
                    _progress.SetProgress(81, "Повторно ожидаем обнаружение ККТ в ТС ПИоТ...");
                    if (!await WaitForKktAsync(cancellationToken).ConfigureAwait(false))
                    {
                        return KktBootstrapResult.Create(KktBootstrapStatus.KktNotVisibleInEsm,
                            "ККТ отвечает драйверу АТОЛ, но ТС ПИоТ не обнаружил её даже после перезапуска ESM.");
                    }
                }

                _progress.SetProgress(84, "Регистрируем ТС ПИоТ...");
                TsPiotRegistrationResult registration = await _registrationWorkflow
                    .RegisterAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!registration.IsSuccess)
                    return KktBootstrapResult.Create(KktBootstrapStatus.RegistrationFailed,
                        "ККТ обнаружена, но регистрацию ТС ПИоТ завершить не удалось.");

                _progress.SetProgress(88, "ТС ПИоТ успешно зарегистрирован.");
                if (!await _confirmGisMtWait(cancellationToken).ConfigureAwait(false))
                {
                    return KktBootstrapResult.Create(
                        KktBootstrapStatus.RegistrationSuccessChannelSkipped,
                        "ТС ПИоТ успешно зарегистрирован. Ожидание контролируемого канала пропущено.");
                }

                return await WaitForGisMtAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return KktBootstrapResult.Create(KktBootstrapStatus.Cancelled, "Подготовка ККТ отменена.");
            }
            catch (Exception ex)
            {
                return KktBootstrapResult.Create(KktBootstrapStatus.HelperFailed,
                    "Не удалось завершить подготовку ККТ: " + ex.Message);
            }
            finally
            {
                if (session != null)
                {
                    try { await session.CloseAsync(CancellationToken.None).ConfigureAwait(false); }
                    catch { }
                    try { await session.DisposeAsync().ConfigureAwait(false); }
                    catch { }
                }
            }
        }

        private async Task<bool> WaitForKktAsync(CancellationToken cancellationToken)
        {
            DateTimeOffset deadline = _utcNow() + _options.KktDiscoveryTimeout;
            using var phaseTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            phaseTimeout.CancelAfter(_options.KktDiscoveryTimeout);
            try
            {
                do
                {
                    phaseTimeout.Token.ThrowIfCancellationRequested();
                    EsmCashRegisterResult result = await _esmStatusClient
                        .GetCashRegisterStatusAsync(phaseTimeout.Token)
                        .ConfigureAwait(false);
                    if (result.Kind == EsmCashRegisterResultKind.Connected)
                        return true;
                    if (_utcNow() >= deadline)
                        return false;
                    await _delay(_options.KktPollInterval, phaseTimeout.Token).ConfigureAwait(false);
                } while (_utcNow() < deadline);
                return false;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        private async Task<KktBootstrapResult> WaitForGisMtAsync(CancellationToken cancellationToken)
        {
            DateTimeOffset started = _utcNow();
            DateTimeOffset deadline = started + _options.GisMtTimeout;
            DiagnosticsSnapshot last = null;
            using var phaseTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            phaseTimeout.CancelAfter(_options.GisMtTimeout);
            try
            {
                while (_utcNow() < deadline)
                {
                    phaseTimeout.Token.ThrowIfCancellationRequested();
                    TimeSpan elapsed = _utcNow() - started;
                    _progress.SetProgress(92,
                        $"Ожидаем создание контролируемого канала связи с ГИС МТ... " +
                        $"Соединение с ККТ поддерживается автоматически. Максимальное ожидание — 5 минут. " +
                        $"Прошло {Math.Max(0, (int)elapsed.TotalSeconds)} сек.");
                    last = await _refreshDiagnostics(phaseTimeout.Token).ConfigureAwait(false);
                    if (last?.Gismt?.State == DiagnosticState.Healthy)
                    {
                        _progress.SetProgress(100, "Точка готова к работе.");
                        return KktBootstrapResult.Create(KktBootstrapStatus.ChannelReady,
                            "Точка готова к работе. ТС ПИоТ зарегистрирован, контролируемый канал связи с ГИС МТ работает.", last);
                    }
                    await _delay(_options.GisMtPollInterval, phaseTimeout.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }

            return KktBootstrapResult.Create(KktBootstrapStatus.RegistrationSuccessChannelTimeout,
                "ТС ПИоТ успешно зарегистрирован, но за 5 минут контролируемый канал связи с ГИС МТ не перешёл в состояние готовности.", last);
        }

        private static KktBootstrapResult StartFailure(KktBootstrapStartResult start) => start.Status switch
        {
            KktBootstrapStartStatus.PortBusy => KktBootstrapResult.Create(KktBootstrapStatus.KktPortBusy,
                "ККТ уже используется другой программой. Закройте товароучётную систему и повторите попытку."),
            KktBootstrapStartStatus.DriverVersionMismatch => KktBootstrapResult.Create(KktBootstrapStatus.DriverVersionMismatch,
                string.IsNullOrWhiteSpace(start.Message) ? "Версия драйвера АТОЛ не соответствует настройкам рабочей точки." : start.Message),
            _ => KktBootstrapResult.Create(KktBootstrapStatus.KktConnectionFailed,
                string.IsNullOrWhiteSpace(start.Message) ? "Не удалось подключиться к ККТ." : start.Message)
        };

        private static string NormalizeArchitecture(string value)
        {
            if (string.Equals(value, "x86", StringComparison.OrdinalIgnoreCase) || value == "86") return "x86";
            if (string.Equals(value, "x64", StringComparison.OrdinalIgnoreCase) || value == "64") return "x64";
            throw new InvalidOperationException("Сервер передал неподдерживаемую архитектуру драйвера АТОЛ.");
        }
    }
}
