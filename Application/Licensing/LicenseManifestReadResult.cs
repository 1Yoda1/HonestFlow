using HonestFlow.Models.Licensing;
using System;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseManifestReadResult
    {
        private LicenseManifestReadResult(
            LicenseManifestReadStatus status,
            LicenseGrant grant,
            string errorCode,
            byte[] grantBytes,
            byte[] signatureFileBytes,
            DateTimeOffset? serverDateUtc,
            bool cacheable)
        {
            Status = status;
            Grant = grant;
            ErrorCode = errorCode;
            GrantBytes = grantBytes;
            SignatureFileBytes = signatureFileBytes;
            ServerDateUtc = serverDateUtc?.ToUniversalTime();
            Cacheable = cacheable;
        }

        public LicenseManifestReadStatus Status { get; }
        public LicenseGrant Grant { get; }
        public string ErrorCode { get; }
        public ReadOnlyMemory<byte> GrantBytes { get; }
        public ReadOnlyMemory<byte> SignatureFileBytes { get; }
        public DateTimeOffset? ServerDateUtc { get; }
        public bool Cacheable { get; }
        public bool IsSuccess => Status == LicenseManifestReadStatus.Success;

        public static LicenseManifestReadResult Success(
            LicenseGrant grant,
            byte[] grantBytes,
            byte[] signatureFileBytes,
            DateTimeOffset? serverDateUtc = null,
            bool cacheable = true) =>
            new(
                LicenseManifestReadStatus.Success,
                grant,
                null,
                grantBytes == null ? null : (byte[])grantBytes.Clone(),
                signatureFileBytes == null ? null : (byte[])signatureFileBytes.Clone(),
                serverDateUtc,
                cacheable);

        public static LicenseManifestReadResult Failure(
            LicenseManifestReadStatus status,
            string errorCode) => new(status, null, errorCode, null, null, null, false);
    }
}
