using System;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.PointStatus;
using HonestFlow.Models;

namespace HonestFlow.Application.RemoteAccess
{
    public sealed class HelpRequestWorkflow
    {
        private readonly IPointStatusService _pointStatusService;
        private readonly HelpRequestDataBuilder _dataBuilder;
        private readonly HelpRequestDeliveryService _deliveryService;

        public HelpRequestWorkflow(
            IPointStatusService pointStatusService,
            HelpRequestDataBuilder dataBuilder,
            HelpRequestDeliveryService deliveryService)
        {
            _pointStatusService = pointStatusService ?? throw new ArgumentNullException(nameof(pointStatusService));
            _dataBuilder = dataBuilder ?? throw new ArgumentNullException(nameof(dataBuilder));
            _deliveryService = deliveryService ?? throw new ArgumentNullException(nameof(deliveryService));
        }

        public async Task<HelpRequestWorkflowResult> SendAsync(
            HelpRequestWorkflowInput input,
            CancellationToken cancellationToken)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            PointStatusResult pointStatus = null;
            string pointStatusError = null;
            DateTimeOffset checkedAt = DateTimeOffset.Now;
            try
            {
                pointStatus = await _pointStatusService.CheckAsync(cancellationToken);
                checkedAt = DateTimeOffset.Now;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                pointStatusError = ex.Message;
            }

            HelpRequestData request = _dataBuilder.Build(new HelpRequestDataContext
            {
                SelectedClient = input.SelectedClient,
                LastClient = input.LastClient,
                RuDesktopId = input.RuDesktopId,
                HonestFlowVersion = input.HonestFlowVersion,
                FiscalAddress = input.FiscalAddress,
                ProblemType = input.ProblemType,
                Message = input.Message,
                PointStatus = pointStatus,
                PointStatusCheckedAt = checkedAt,
                PointStatusError = pointStatusError,
                LicenseSnapshot = input.LicenseSnapshot
            });
            string clientId = input.SelectedClient?.ClientId ?? input.LastClient?.ClientId;
            bool hasActiveLicense = input.LicenseSnapshot?.Decision == LicenseDecision.Allowed &&
                string.Equals(input.LicenseSnapshot.ClientId, clientId, StringComparison.Ordinal);
            await _deliveryService.SendAsync(
                request,
                hasActiveLicense,
                clientId,
                input.LicenseSnapshot?.DeviceId,
                cancellationToken);

            return new HelpRequestWorkflowResult(request, pointStatus, pointStatusError);
        }
    }

    public sealed class HelpRequestWorkflowInput
    {
        public IPData SelectedClient { get; init; }
        public LastAuthorizedClientState LastClient { get; init; }
        public string RuDesktopId { get; init; }
        public string HonestFlowVersion { get; init; }
        public string FiscalAddress { get; init; }
        public string ProblemType { get; init; }
        public string Message { get; init; }
        public LicenseObservationSnapshot LicenseSnapshot { get; init; }
    }

    public sealed class HelpRequestWorkflowResult
    {
        public HelpRequestWorkflowResult(
            HelpRequestData request,
            PointStatusResult pointStatus,
            string pointStatusError)
        {
            Request = request;
            PointStatus = pointStatus;
            PointStatusError = pointStatusError;
        }
        public HelpRequestData Request { get; }
        public PointStatusResult PointStatus { get; }
        public string PointStatusError { get; }
    }
}
