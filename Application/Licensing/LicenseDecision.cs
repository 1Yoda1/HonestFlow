namespace HonestFlow.Application.Licensing
{
    public enum LicenseDecision
    {
        Allowed,
        ClientNotFound,
        ClientDisabled,
        DeviceNotRegistered,
        LicenseNotIssued,
        DeviceDisabled,
        VersionTooOld,
        ManifestExpired,
        OfflineGraceExpired,
        InvalidLicenseState
    }
}
