namespace HonestFlow.Application.Auth
{
    public enum LicenseAuthenticationStage
    {
        CheckingPassword,
        ClientResolved,
        CheckingDeviceAndLicense,
        Completed
    }

    public sealed class LicenseAuthenticationProgress
    {
        public LicenseAuthenticationProgress(
            LicenseAuthenticationStage stage,
            string clientName = null)
        {
            Stage = stage;
            ClientName = clientName;
        }

        public LicenseAuthenticationStage Stage { get; }
        public string ClientName { get; }
    }
}
