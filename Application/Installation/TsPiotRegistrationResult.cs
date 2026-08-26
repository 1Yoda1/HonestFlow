namespace HonestFlow.Application.Installation
{
    public enum TsPiotRegistrationStatus
    {
        Success,
        EsmUnavailable,
        KktNotDetected,
        RegistrationFailed
    }

    public sealed class TsPiotRegistrationResult
    {
        private TsPiotRegistrationResult(TsPiotRegistrationStatus status, string technicalReason)
        {
            Status = status;
            TechnicalReason = technicalReason;
        }

        public TsPiotRegistrationStatus Status { get; }
        public string TechnicalReason { get; }
        public bool IsSuccess => Status == TsPiotRegistrationStatus.Success;

        public static TsPiotRegistrationResult Success() =>
            new(TsPiotRegistrationStatus.Success, null);

        public static TsPiotRegistrationResult EsmUnavailable(string technicalReason = null) =>
            new(TsPiotRegistrationStatus.EsmUnavailable, technicalReason);

        public static TsPiotRegistrationResult KktNotDetected(string technicalReason = null) =>
            new(TsPiotRegistrationStatus.KktNotDetected, technicalReason);

        public static TsPiotRegistrationResult RegistrationFailed(string technicalReason = null) =>
            new(TsPiotRegistrationStatus.RegistrationFailed, technicalReason);
    }
}
