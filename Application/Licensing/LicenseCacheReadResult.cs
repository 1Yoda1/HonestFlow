using System;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseCacheReadResult
    {
        private LicenseCacheReadResult(
            LicenseCacheStatus status,
            LicenseGrant grant,
            DateTimeOffset? lastSuccessfulOnlineCheckUtc,
            string errorCode)
        {
            Status = status;
            Grant = grant;
            LastSuccessfulOnlineCheckUtc = lastSuccessfulOnlineCheckUtc;
            ErrorCode = errorCode;
        }

        public LicenseCacheStatus Status { get; }
        public LicenseGrant Grant { get; }
        public DateTimeOffset? LastSuccessfulOnlineCheckUtc { get; }
        public string ErrorCode { get; }
        public bool IsSuccess => Status == LicenseCacheStatus.Success;

        public static LicenseCacheReadResult Success(
            LicenseGrant grant,
            DateTimeOffset lastSuccessfulOnlineCheckUtc) =>
            new(LicenseCacheStatus.Success, grant, lastSuccessfulOnlineCheckUtc, null);

        public static LicenseCacheReadResult Failure(
            LicenseCacheStatus status,
            string errorCode) => new(status, null, null, errorCode);
    }
}
