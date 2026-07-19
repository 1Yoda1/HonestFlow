namespace HonestFlow.Application.Prerequisites
{
    public enum DotNetRuntimeInstallStatus
    {
        AlreadyInstalled,
        Success,
        RebootRequired,
        UnsupportedArchitecture,
        InsufficientDiskSpace,
        AssetNotFound,
        DownloadFailed,
        InvalidPackage,
        InstallationFailed,
        UnexpectedError
    }
}
