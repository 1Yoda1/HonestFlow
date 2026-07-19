namespace HonestFlow.Application.RemoteAccess
{
    public enum RuDesktopInstallStatus
    {
        Success,
        RebootRequired,
        AssetNotFound,
        DownloadFailed,
        InvalidPackage,
        UserCancelled,
        InstallationFailed,
        UnexpectedError
    }

    public sealed class RuDesktopInstallResult
    {
        public RuDesktopInstallStatus Status { get; set; }
        public int? ExitCode { get; set; }
        public string Message { get; set; }
        public bool IsSuccess => Status == RuDesktopInstallStatus.Success ||
                                 Status == RuDesktopInstallStatus.RebootRequired;
    }
}
