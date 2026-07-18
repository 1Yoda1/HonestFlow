namespace HonestFlow.Application.Licensing
{
    public enum LicenseManifestReadStatus
    {
        Success,
        NetworkUnavailable,
        Timeout,
        NotFound,
        InvalidJson,
        InvalidManifest,
        UnsupportedSchema,
        InvalidSignature,
        UnknownKey,
        ServerError
    }
}
