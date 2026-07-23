namespace HonestFlow.Application.PointStatus
{
    public enum EsmRegistrationResultKind
    {
        Registered,
        NotConfigured,
        Unavailable
    }

    public sealed class EsmRegistrationResult
    {
        private EsmRegistrationResult(EsmRegistrationResultKind kind)
        {
            Kind = kind;
        }

        public EsmRegistrationResultKind Kind { get; }

        public static EsmRegistrationResult Registered() => new(EsmRegistrationResultKind.Registered);
        public static EsmRegistrationResult NotConfigured() => new(EsmRegistrationResultKind.NotConfigured);
        public static EsmRegistrationResult Unavailable() => new(EsmRegistrationResultKind.Unavailable);
    }
}
