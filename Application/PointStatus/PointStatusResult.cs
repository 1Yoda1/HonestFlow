namespace HonestFlow.Application.PointStatus
{
    public sealed class PointStatusResult
    {
        public NodeStatus Lm { get; set; }
        public NodeStatus Controller { get; set; }
        public NodeStatus ControllerServiceStatus { get; set; }
        public ControllerServiceInfoResult ControllerServiceInfo { get; set; }
        public NodeStatus Esm { get; set; }
        public NodeStatus Kkt { get; set; }
        public NodeStatus Cloud { get; set; }
        public NodeStatus RuDesktop { get; set; }
        public EsmStatusResult EsmApiStatus { get; set; }
        public EsmRegistrationResult EsmRegistration { get; set; }
        public EsmCashRegisterResult CashRegister { get; set; }
        public NodeStatus KktServiceStatus { get; set; }
        public KktDriverProbeResult KktDriver { get; set; }
        public KktPortProbeResult KktPort4041 { get; set; }
        public string AtolDriverVersion { get; set; }
        public KktPnpResult KktPnP { get; set; }
        public LmDiagnosticProbeResult LmProbe { get; set; }
        public ServiceSnapshot[] ServiceSnapshots { get; set; } = System.Array.Empty<ServiceSnapshot>();
    }
}
