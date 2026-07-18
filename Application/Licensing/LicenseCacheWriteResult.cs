namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseCacheWriteResult
    {
        private LicenseCacheWriteResult(LicenseCacheStatus status, string errorCode)
        {
            Status = status;
            ErrorCode = errorCode;
        }

        public LicenseCacheStatus Status { get; }
        public string ErrorCode { get; }
        public bool IsSuccess => Status == LicenseCacheStatus.Success;

        public static LicenseCacheWriteResult Success() =>
            new(LicenseCacheStatus.Success, null);

        public static LicenseCacheWriteResult Failure(
            LicenseCacheStatus status,
            string errorCode) => new(status, errorCode);
    }
}
