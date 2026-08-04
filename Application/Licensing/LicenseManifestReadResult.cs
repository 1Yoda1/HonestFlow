using HonestFlow.Models.Licensing;
using System;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseManifestReadResult
    {
        private LicenseManifestReadResult(
            LicenseManifestReadStatus status,
            LicenseManifest manifest,
            string errorCode,
            byte[] manifestBytes,
            byte[] signatureFileBytes,
            DateTimeOffset? serverDateUtc)
        {
            Status = status;
            Manifest = manifest;
            ErrorCode = errorCode;
            ManifestBytes = manifestBytes;
            SignatureFileBytes = signatureFileBytes;
            ServerDateUtc = serverDateUtc?.ToUniversalTime();
        }

        public LicenseManifestReadStatus Status { get; }
        public LicenseManifest Manifest { get; }
        public string ErrorCode { get; }
        public ReadOnlyMemory<byte> ManifestBytes { get; }
        public ReadOnlyMemory<byte> SignatureFileBytes { get; }
        public DateTimeOffset? ServerDateUtc { get; }
        public bool IsSuccess => Status == LicenseManifestReadStatus.Success;

        public static LicenseManifestReadResult Success(
            LicenseManifest manifest,
            byte[] manifestBytes,
            byte[] signatureFileBytes,
            DateTimeOffset? serverDateUtc = null) =>
            new(
                LicenseManifestReadStatus.Success,
                manifest,
                null,
                manifestBytes == null ? null : (byte[])manifestBytes.Clone(),
                signatureFileBytes == null ? null : (byte[])signatureFileBytes.Clone(),
                serverDateUtc);

        public static LicenseManifestReadResult Failure(
            LicenseManifestReadStatus status,
            string errorCode) => new(status, null, errorCode, null, null, null);
    }
}
