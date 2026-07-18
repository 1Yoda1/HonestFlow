namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseAccessResult
    {
        public LicenseAccessResult(bool isAllowed, string technicalCode, string message)
        {
            IsAllowed = isAllowed;
            TechnicalCode = technicalCode;
            Message = message;
        }

        public bool IsAllowed { get; }
        public string TechnicalCode { get; }
        public string Message { get; }
    }
}
