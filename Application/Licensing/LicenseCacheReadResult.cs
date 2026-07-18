using System;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseCacheReadResult
    {
        private LicenseCacheReadResult(
            LicenseCacheStatus status,
            LicenseManifest manifest,
            DateTimeOffset? lastSuccessfulOnlineCheckUtc,
            string errorCode)
        {
            Status = status;
            Manifest = manifest;
            LastSuccessfulOnlineCheckUtc = lastSuccessfulOnlineCheckUtc;
            ErrorCode = errorCode;
        }

        public LicenseCacheStatus Status { get; }
        public LicenseManifest Manifest { get; }
        public DateTimeOffset? LastSuccessfulOnlineCheckUtc { get; }
        public string ErrorCode { get; }
        public bool IsSuccess => Status == LicenseCacheStatus.Success;

        public static LicenseCacheReadResult Success(
            LicenseManifest manifest,
            DateTimeOffset lastSuccessfulOnlineCheckUtc) =>
            new(LicenseCacheStatus.Success, manifest, lastSuccessfulOnlineCheckUtc, null);

        public static LicenseCacheReadResult Failure(
            LicenseCacheStatus status,
            string errorCode) => new(status, null, null, errorCode);
    }
}
