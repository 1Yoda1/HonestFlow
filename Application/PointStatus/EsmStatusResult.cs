namespace HonestFlow.Application.PointStatus
{
    public enum EsmStatusResultKind
    {
        Success,
        NotConfigured,
        Unavailable
    }

    public sealed class EsmStatusResult
    {
        private EsmStatusResult(EsmStatusResultKind kind, EsmStatusDto status)
        {
            Kind = kind;
            Status = status;
        }

        public EsmStatusResultKind Kind { get; }
        public EsmStatusDto Status { get; }

        public static EsmStatusResult Success(EsmStatusDto status) =>
            new(EsmStatusResultKind.Success, status);

        public static EsmStatusResult NotConfigured() =>
            new(EsmStatusResultKind.NotConfigured, null);

        public static EsmStatusResult Unavailable() =>
            new(EsmStatusResultKind.Unavailable, null);
    }
}
