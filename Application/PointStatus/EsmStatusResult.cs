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
        private EsmStatusResult(EsmStatusResultKind kind, EsmStatusDto status, int? apiPort, string instanceId)
        {
            Kind = kind;
            Status = status;
            ApiPort = apiPort;
            InstanceId = instanceId;
        }

        public EsmStatusResultKind Kind { get; }
        public EsmStatusDto Status { get; }
        public int? ApiPort { get; }
        public string InstanceId { get; }

        public static EsmStatusResult Success(EsmStatusDto status, int? apiPort = null, string instanceId = null) =>
            new(EsmStatusResultKind.Success, status, apiPort, instanceId);

        public static EsmStatusResult NotConfigured(int? apiPort = null) =>
            new(EsmStatusResultKind.NotConfigured, null, apiPort, null);

        public static EsmStatusResult Unavailable(int? apiPort = null) =>
            new(EsmStatusResultKind.Unavailable, null, apiPort, null);
    }
}
