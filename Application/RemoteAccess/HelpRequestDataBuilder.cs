using System;
using System.Linq;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.PointStatus;
using HonestFlow.Infrastructure;
using HonestFlow.Models;

namespace HonestFlow.Application.RemoteAccess
{
    public sealed class HelpRequestDataBuilder
    {
        private readonly Func<DateTimeOffset> _now;
        private readonly Func<bool> _isAdministrator;

        public HelpRequestDataBuilder()
            : this(() => DateTimeOffset.Now, Utils.IsAdministrator)
        {
        }

        public HelpRequestDataBuilder(
            Func<DateTimeOffset> now,
            Func<bool> isAdministrator)
        {
            _now = now ?? throw new ArgumentNullException(nameof(now));
            _isAdministrator = isAdministrator ?? throw new ArgumentNullException(nameof(isAdministrator));
        }

        public HelpRequestData Build(HelpRequestDataContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            IPData selectedClient = context.SelectedClient;
            LastAuthorizedClientState lastClient = context.LastClient;
            LicenseObservationSnapshot licenseSnapshot = context.LicenseSnapshot;
            DateTimeOffset createdAt = _now();

            return new HelpRequestData
            {
                RequestId = BuildRequestId(createdAt),
                ClientName = ValueOrDash(selectedClient?.Name ?? lastClient?.Name),
                InnMasked = MaskInn(selectedClient?.Inn ?? lastClient?.Inn),
                MachineName = Environment.MachineName,
                WindowsUser = Environment.UserName,
                RuDesktopId = ValueOrDash(context.RuDesktopId),
                HonestFlowVersion = context.HonestFlowVersion,
                OsVersion = Environment.OSVersion.ToString(),
                Architecture = $"ОС {(Environment.Is64BitOperatingSystem ? "x64" : "x86")}, процесс {(Environment.Is64BitProcess ? "x64" : "x86")}",
                IsAdministrator = _isAdministrator(),
                ClientId = selectedClient?.ClientId ?? lastClient?.ClientId,
                DeviceId = licenseSnapshot?.DeviceId,
                LicenseDecision = licenseSnapshot?.Decision.ToString(),
                LicenseTechnicalCode = licenseSnapshot?.TechnicalCode,
                LicenseSource = licenseSnapshot?.ManifestSource?.ToString(),
                LicenseRevision = licenseSnapshot?.Revision,
                FiscalAddress = ValueOrDash(context.FiscalAddress),
                ProblemType = ValueOrDash(context.ProblemType),
                Message = ValueOrDash(context.Message),
                CreatedAt = createdAt.ToString("o"),
                PointStatus = BuildPointStatus(
                    context.PointStatus,
                    context.PointStatusCheckedAt,
                    context.PointStatusError)
            };
        }

        private static HelpRequestPointStatus BuildPointStatus(
            PointStatusResult result,
            DateTimeOffset checkedAt,
            string error)
        {
            return new HelpRequestPointStatus
            {
                CheckedAt = checkedAt.ToString("o"),
                Error = error,
                Lm = BuildNodeStatus(result?.Lm),
                Controller = BuildNodeStatus(result?.Controller),
                Esm = BuildNodeStatus(result?.Esm),
                Kkt = BuildNodeStatus(result?.Kkt),
                Cloud = BuildNodeStatus(result?.Cloud),
                RuDesktop = BuildNodeStatus(result?.RuDesktop)
            };
        }

        private static HelpRequestNodeStatus BuildNodeStatus(NodeStatus status)
        {
            if (status == null)
                return null;

            return new HelpRequestNodeStatus
            {
                Level = status.Level.ToString(),
                ShortText = status.ShortText,
                StatusText = status.StatusText,
                Details = status.Details,
                Services = status.Services
                    .Select(service => new HelpRequestServiceStatus
                    {
                        Name = service.ServiceName,
                        State = service.State
                    })
                    .ToArray()
            };
        }

        private static string BuildRequestId(DateTimeOffset timestamp) =>
            $"{timestamp:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

        private static string MaskInn(string inn)
        {
            if (string.IsNullOrWhiteSpace(inn))
                return "-";

            inn = inn.Trim();
            if (inn.Length <= 4)
                return new string('*', inn.Length);

            int left = Math.Min(2, inn.Length);
            int right = Math.Min(2, inn.Length - left);
            return inn[..left] +
                   new string('*', Math.Max(0, inn.Length - left - right)) +
                   inn[^right..];
        }

        private static string ValueOrDash(string value) =>
            string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
    }

    public sealed class HelpRequestDataContext
    {
        public IPData SelectedClient { get; init; }
        public LastAuthorizedClientState LastClient { get; init; }
        public string RuDesktopId { get; init; }
        public string HonestFlowVersion { get; init; }
        public string FiscalAddress { get; init; }
        public string ProblemType { get; init; }
        public string Message { get; init; }
        public PointStatusResult PointStatus { get; init; }
        public DateTimeOffset PointStatusCheckedAt { get; init; }
        public string PointStatusError { get; init; }
        public LicenseObservationSnapshot LicenseSnapshot { get; init; }
    }
}
