namespace HonestFlow.Application.RemoteAccess
{
    public class RuDesktopStatus
    {
        public bool IsInstalled { get; set; }
        public bool ServiceInstalled { get; set; }
        public bool ServiceRunning { get; set; }
        public string Id { get; set; }
        public bool PasswordConfiguredByHonestFlow { get; set; }
        public string ErrorMessage { get; set; }
    }
}
