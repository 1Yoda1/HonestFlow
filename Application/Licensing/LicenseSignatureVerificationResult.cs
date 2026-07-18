namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseSignatureVerificationResult
    {
        private LicenseSignatureVerificationResult(
            LicenseSignatureVerificationStatus status,
            string errorCode)
        {
            Status = status;
            ErrorCode = errorCode;
        }

        public LicenseSignatureVerificationStatus Status { get; }
        public string ErrorCode { get; }
        public bool IsValid => Status == LicenseSignatureVerificationStatus.Valid;

        public static LicenseSignatureVerificationResult Valid() =>
            new(LicenseSignatureVerificationStatus.Valid, null);

        public static LicenseSignatureVerificationResult Failure(
            LicenseSignatureVerificationStatus status,
            string errorCode) => new(status, errorCode);
    }
}
