namespace HonestFlow.Models
{
    public class LmValidationResult
    {
        public bool IsPhysicallyInstalled { get; set; }
        public string PhysicalVersion { get; set; }
        public string RuntimeStatus { get; set; }
        public string DiagnosticStatus { get; set; }
        public LmStatus ApiStatus { get; set; }
        public bool NeedsInstall { get; set; }
        public bool NeedsInitialize { get; set; }
        public string DisplayStatus { get; set; }
        public string DecisionReason { get; set; }
    }
}
