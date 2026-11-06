namespace HonestFlow.Application.PointStatus
{
    public enum EsmStatusResultKind
    {
        Success,
        NotConfigured,
        Unavailable,
        UnknownSchema
    }

    public sealed class EsmStatusResult
    {
        private EsmStatusResult(EsmStatusResultKind kind, EsmStatusDto status, int? apiPort, string instanceId, string schemaName)
        {
            Kind = kind;
            Status = status;
            ApiPort = apiPort;
            InstanceId = instanceId;
            SchemaName = schemaName;
        }

        public EsmStatusResultKind Kind { get; }
        public EsmStatusDto Status { get; }
        public int? ApiPort { get; }
        public string InstanceId { get; }
        public string SchemaName { get; }

        public static EsmStatusResult Success(EsmStatusDto status, int? apiPort = null, string instanceId = null, string schemaName = null) =>
            new(EsmStatusResultKind.Success, status, apiPort, instanceId, schemaName);

        public static EsmStatusResult NotConfigured(int? apiPort = null) =>
            new(EsmStatusResultKind.NotConfigured, null, apiPort, null, null);

        public static EsmStatusResult Unavailable(int? apiPort = null) =>
            new(EsmStatusResultKind.Unavailable, null, apiPort, null, null);

        public static EsmStatusResult UnknownSchema(int? apiPort = null, string instanceId = null) =>
            new(EsmStatusResultKind.UnknownSchema, null, apiPort, instanceId, null);
    }
}
