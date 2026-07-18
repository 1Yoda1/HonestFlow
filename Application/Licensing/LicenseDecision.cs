namespace HonestFlow.Application.Licensing
{
    public enum LicenseDecision
    {
        Allowed,
        ClientNotFound,
        ClientDisabled,
        DeviceNotRegistered,
        DeviceDisabled,
        VersionTooOld,
        ManifestExpired,
        OfflineGraceExpired,
        InvalidLicenseState
    }
}
