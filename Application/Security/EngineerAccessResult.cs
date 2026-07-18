using System;

namespace HonestFlow.Application.Security
{
    public sealed class EngineerAccessResult
    {
        public EngineerAccessResult(
            bool isAllowed,
            bool passwordRequired,
            string technicalCode,
            string message,
            DateTimeOffset? unlockedUntilUtc = null,
            DateTimeOffset? retryAfterUtc = null)
        {
            IsAllowed = isAllowed;
            PasswordRequired = passwordRequired;
            TechnicalCode = technicalCode;
            Message = message;
            UnlockedUntilUtc = unlockedUntilUtc;
            RetryAfterUtc = retryAfterUtc;
        }

        public bool IsAllowed { get; }
        public bool PasswordRequired { get; }
        public string TechnicalCode { get; }
        public string Message { get; }
        public DateTimeOffset? UnlockedUntilUtc { get; }
        public DateTimeOffset? RetryAfterUtc { get; }
    }
}
