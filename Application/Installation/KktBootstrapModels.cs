using System;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.PointStatus;

namespace HonestFlow.Application.Installation
{
    public enum KktBootstrapStatus
    {
        ChannelReady,
        RegistrationSuccessChannelSkipped,
        RegistrationSuccessChannelTimeout,
        KktPortBusy,
        KktConnectionFailed,
        KktNotVisibleInEsm,
        RegistrationFailed,
        DriverVersionMismatch,
        HelperFailed,
        Cancelled
    }

    public sealed class KktBootstrapResult
    {
        private KktBootstrapResult(
            KktBootstrapStatus status,
            string message,
            DiagnosticsSnapshot lastDiagnostics = null)
        {
            Status = status;
            Message = message ?? string.Empty;
            LastDiagnostics = lastDiagnostics;
        }

        public KktBootstrapStatus Status { get; }
        public string Message { get; }
        public DiagnosticsSnapshot LastDiagnostics { get; }
        public bool RegistrationSucceeded => Status is
            KktBootstrapStatus.ChannelReady or
            KktBootstrapStatus.RegistrationSuccessChannelSkipped or
            KktBootstrapStatus.RegistrationSuccessChannelTimeout;
        public bool IsSuccessful => RegistrationSucceeded;

        public static KktBootstrapResult Create(
            KktBootstrapStatus status,
            string message,
            DiagnosticsSnapshot diagnostics = null) =>
            new(status, message, diagnostics);
    }

    public enum KktBootstrapStartStatus
    {
        Connected,
        PortBusy,
        DriverVersionMismatch,
        Failed
    }

    public sealed class KktBootstrapConnectionInfo
    {
        public string Architecture { get; init; }
        public string DriverVersion { get; init; }
        public string Model { get; init; }
        public string MaskedSerial { get; init; }
        public string Firmware { get; init; }
    }

    public sealed class KktBootstrapStartResult
    {
        private KktBootstrapStartResult(
            KktBootstrapStartStatus status,
            IKktBootstrapSession session,
            KktBootstrapConnectionInfo connection,
            string errorCode,
            string message)
        {
            Status = status;
            Session = session;
            Connection = connection;
            ErrorCode = errorCode;
            Message = message ?? string.Empty;
        }

        public KktBootstrapStartStatus Status { get; }
        public IKktBootstrapSession Session { get; }
        public KktBootstrapConnectionInfo Connection { get; }
        public string ErrorCode { get; }
        public string Message { get; }

        public static KktBootstrapStartResult Connected(
            IKktBootstrapSession session,
            KktBootstrapConnectionInfo connection) =>
            new(KktBootstrapStartStatus.Connected, session, connection, null, null);

        public static KktBootstrapStartResult Error(
            KktBootstrapStartStatus status,
            string errorCode,
            string message) =>
            new(status, null, null, errorCode, message);
    }

    public interface IKktBootstrapProcessClient
    {
        Task<KktBootstrapStartResult> StartAsync(
            string architecture,
            string requiredDriverVersion,
            CancellationToken cancellationToken);
    }

    public interface IKktBootstrapSession : IAsyncDisposable
    {
        bool IsRunning { get; }
        Task CloseAsync(CancellationToken cancellationToken);
    }

    public sealed class KktBootstrapWorkflowOptions
    {
        public static KktBootstrapWorkflowOptions Default { get; } = new();
        public TimeSpan KktDiscoveryTimeout { get; init; } = TimeSpan.FromSeconds(30);
        public TimeSpan KktPollInterval { get; init; } = TimeSpan.FromSeconds(1);
        public TimeSpan GisMtTimeout { get; init; } = TimeSpan.FromMinutes(5);
        public TimeSpan GisMtPollInterval { get; init; } = TimeSpan.FromSeconds(2);
    }
}
