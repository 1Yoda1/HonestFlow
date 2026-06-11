namespace HonestFlow.Services.Installation.Planning
{
    public class ComponentPlanItem
    {
        public InstallationComponent Component { get; set; }
        public bool NeedInstall { get; set; }
        public string DisplayName { get; set; }
        public string StatusText { get; set; }
        public string ExpectedVersion { get; set; }
        public string FileName { get; set; }
        public string InstallerPath { get; set; }
    }
}
