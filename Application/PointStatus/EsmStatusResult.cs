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
        private EsmStatusResult(EsmStatusResultKind kind, EsmStatusDto status, int? apiPort)
        {
            Kind = kind;
            Status = status;
            ApiPort = apiPort;
        }

        public EsmStatusResultKind Kind { get; }
        public EsmStatusDto Status { get; }
        public int? ApiPort { get; }

        public static EsmStatusResult Success(EsmStatusDto status, int? apiPort = null) =>
            new(EsmStatusResultKind.Success, status, apiPort);

        public static EsmStatusResult NotConfigured(int? apiPort = null) =>
            new(EsmStatusResultKind.NotConfigured, null, apiPort);

        public static EsmStatusResult Unavailable(int? apiPort = null) =>
            new(EsmStatusResultKind.Unavailable, null, apiPort);
    }
}
