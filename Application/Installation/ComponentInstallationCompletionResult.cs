namespace HonestFlow.Application.Installation
{
    public sealed class ComponentInstallationCompletionResult
    {
        private ComponentInstallationCompletionResult(bool componentsInstalled, TsPiotRegistrationResult tsPiotRegistration)
        {
            ComponentsInstalled = componentsInstalled;
            TsPiotRegistration = tsPiotRegistration;
        }

        public bool ComponentsInstalled { get; }
        public TsPiotRegistrationResult TsPiotRegistration { get; }

        public static ComponentInstallationCompletionResult InstallationFailed() =>
            new(false, null);

        public static ComponentInstallationCompletionResult Installed(TsPiotRegistrationResult tsPiotRegistration) =>
            new(true, tsPiotRegistration);
    }
}
