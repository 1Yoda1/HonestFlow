namespace HonestFlow.Application.RemoteAccess
{
    public class RuDesktopStatus
    {
        public RuDesktopInstallationState InstallationState { get; set; }
        public bool IsInstalled { get; set; }
        public bool ServiceInstalled { get; set; }
        public bool ServiceRunning { get; set; }
        public string Id { get; set; }
        public bool PasswordConfiguredByHonestFlow { get; set; }
        public string SyncedCredentialId { get; set; }
        public string CredentialClientId { get; set; }
        public string PendingRevocationClientId { get; set; }
        public bool RemoteCredentialSynced { get; set; }
        public bool ServerCredentialActive { get; set; }
        public bool RevocationPending { get; set; }
        public bool LocalAccessDisabled { get; set; }
        public string ErrorMessage { get; set; }
    }
}
