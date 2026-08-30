namespace HonestFlow.Application.Installation
{
    public sealed class ComponentInstallationCompletionResult
    {
        private ComponentInstallationCompletionResult(
            bool componentsInstalled,
            TsPiotRegistrationResult tsPiotRegistration,
            KktBootstrapResult kktBootstrap)
        {
            ComponentsInstalled = componentsInstalled;
            TsPiotRegistration = tsPiotRegistration;
            KktBootstrap = kktBootstrap;
        }

        public bool ComponentsInstalled { get; }
        public TsPiotRegistrationResult TsPiotRegistration { get; }
        public KktBootstrapResult KktBootstrap { get; }
        public bool IsSuccessful => ComponentsInstalled && (KktBootstrap?.IsSuccessful ?? true);

        public static ComponentInstallationCompletionResult InstallationFailed() =>
            new(false, null, null);

        public static ComponentInstallationCompletionResult Installed(TsPiotRegistrationResult tsPiotRegistration) =>
            new(true, tsPiotRegistration, null);

        public static ComponentInstallationCompletionResult InstalledWithBootstrap(KktBootstrapResult kktBootstrap) =>
            new(true, null, kktBootstrap);
    }
}
