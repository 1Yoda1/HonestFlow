namespace HonestFlow.Application.Licensing
{
    public enum LicenseCacheStatus
    {
        Success,
        NotFound,
        InvalidCache,
        StaleRevision,
        WriteFailed
    }
}
