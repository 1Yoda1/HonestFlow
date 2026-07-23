namespace HonestFlow.Application.PointStatus
{
    public enum EsmCashRegisterResultKind
    {
        Connected,
        Disconnected,
        NotConfigured,
        Unavailable
    }

    public sealed class EsmCashRegisterResult
    {
        private EsmCashRegisterResult(EsmCashRegisterResultKind kind)
        {
            Kind = kind;
        }

        public EsmCashRegisterResultKind Kind { get; }

        public static EsmCashRegisterResult Connected() => new(EsmCashRegisterResultKind.Connected);
        public static EsmCashRegisterResult Disconnected() => new(EsmCashRegisterResultKind.Disconnected);
        public static EsmCashRegisterResult NotConfigured() => new(EsmCashRegisterResultKind.NotConfigured);
        public static EsmCashRegisterResult Unavailable() => new(EsmCashRegisterResultKind.Unavailable);
    }
}
