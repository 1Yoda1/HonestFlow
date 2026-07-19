namespace HonestFlow.Application.Prerequisites
{
    public sealed class DotNetRuntimeInstallResult
    {
        public DotNetRuntimeInstallStatus Status { get; init; }
        public string Message { get; init; }
        public int? ExitCode { get; init; }
        public long? AvailableDiskBytes { get; init; }
        public bool IsSuccess =>
            Status == DotNetRuntimeInstallStatus.AlreadyInstalled ||
            Status == DotNetRuntimeInstallStatus.Success ||
            Status == DotNetRuntimeInstallStatus.RebootRequired;
    }
}
