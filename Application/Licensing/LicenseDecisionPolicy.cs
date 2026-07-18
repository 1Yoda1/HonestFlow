namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseDecisionPolicy
    {
        public bool AllowDiagnosticsWhenDenied { get; set; }
        public bool AllowSendLogsWhenDenied { get; set; }
    }
}
