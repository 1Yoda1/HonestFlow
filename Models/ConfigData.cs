namespace HonestFlow.Models
{
    public class ConfigData
    {
        public int TimeoutSeconds { get; set; } = 60;
        public int DelayAfterUninstallSeconds { get; set; } = 30;
        public int DelayAfterInstallSeconds { get; set; } = 20;
        public string InstallersFolder { get; set; } = "";
        public bool DebugMode { get; set; } = false;
    }
}