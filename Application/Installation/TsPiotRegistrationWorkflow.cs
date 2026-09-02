using System;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Core;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.PointStatus;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.Installation
{
    public sealed class TsPiotRegistrationWorkflow
    {
        private readonly IEsmTsPiotRegistrationClient _registrationClient;
        private readonly IEsmApiPortProbe _esmApiPortProbe;
        private readonly ILogService _log;
        private readonly ILicenseOperationGuard _operationGuard;

        public TsPiotRegistrationWorkflow(
            IEsmTsPiotRegistrationClient registrationClient,
            IEsmApiPortProbe esmApiPortProbe,
            ILogService log,
            ILicenseOperationGuard operationGuard)
        {
            _registrationClient = registrationClient ?? throw new ArgumentNullException(nameof(registrationClient));
            _esmApiPortProbe = esmApiPortProbe ?? throw new ArgumentNullException(nameof(esmApiPortProbe));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _operationGuard = operationGuard ?? throw new ArgumentNullException(nameof(operationGuard));
        }

        public async Task<TsPiotRegistrationResult> RegisterAsync(CancellationToken cancellationToken)
        {
            _operationGuard.Demand(LicenseOperation.RegisterTsPiot);
            EsmApiPortProbeResult readiness = await _esmApiPortProbe.CheckAsync(cancellationToken).ConfigureAwait(false);
            if (!readiness.IsAvailable)
            {
                _log.LogUser("Автоматическая регистрация ТС ПИоТ не выполнена: локальный API ЕСМ недоступен.", true);
                return TsPiotRegistrationResult.EsmUnavailable(readiness.ErrorCategory);
            }

            TsPiotRegistrationResult result = await _registrationClient.RegisterAsync(cancellationToken).ConfigureAwait(false);
            _log.LogUser(result.Status switch
            {
                TsPiotRegistrationStatus.Success => "ТС ПИоТ автоматически зарегистрирован.",
                TsPiotRegistrationStatus.KktNotDetected => "Автоматическая регистрация ТС ПИоТ не выполнена: ККТ не обнаружена.",
                TsPiotRegistrationStatus.EsmUnavailable => "Автоматическая регистрация ТС ПИоТ не выполнена: локальный API ЕСМ недоступен.",
                _ => "Автоматическая регистрация ТС ПИоТ не выполнена."
            }, !result.IsSuccess);
            return result;
        }
    }
}
