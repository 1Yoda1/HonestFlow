namespace HonestFlow.Application.Licensing
{
    public enum LicenseManifestReadStatus
    {
        Success,
        NetworkUnavailable,
        Timeout,
        Unauthorized,
        Forbidden,
        Gone,
        RateLimited,
        NotFound,
        InvalidJson,
        InvalidManifest,
        UnsupportedSchema,
        InvalidSignature,
        UnknownKey,
        ServerError
    }
}
