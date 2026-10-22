namespace HonestFlow.Application.Installation
{
    public enum ComponentPostInstallationStatus
    {
        None,
        Cancelled,
        Denied,
        Failed
    }

    public sealed class ComponentInstallationCompletionResult
    {
        private ComponentInstallationCompletionResult(
            bool componentsInstalled,
            TsPiotRegistrationResult tsPiotRegistration,
            KktBootstrapResult kktBootstrap,
            ComponentPostInstallationStatus postInstallationStatus = ComponentPostInstallationStatus.None,
            string postInstallationUserMessage = null)
        {
            ComponentsInstalled = componentsInstalled;
            TsPiotRegistration = tsPiotRegistration;
            KktBootstrap = kktBootstrap;
            PostInstallationStatus = postInstallationStatus;
            PostInstallationUserMessage = postInstallationUserMessage;
        }

        public bool ComponentsInstalled { get; }
        public TsPiotRegistrationResult TsPiotRegistration { get; }
        public KktBootstrapResult KktBootstrap { get; }
        public ComponentPostInstallationStatus PostInstallationStatus { get; }
        public string PostInstallationUserMessage { get; }
        public bool IsSuccessful => ComponentsInstalled &&
            PostInstallationStatus == ComponentPostInstallationStatus.None &&
            (KktBootstrap?.IsSuccessful ?? true);

        public static ComponentInstallationCompletionResult InstallationFailed() =>
            new(false, null, null);

        public static ComponentInstallationCompletionResult Installed(TsPiotRegistrationResult tsPiotRegistration) =>
            new(true, tsPiotRegistration, null);

        public static ComponentInstallationCompletionResult InstalledWithBootstrap(KktBootstrapResult kktBootstrap) =>
            new(true, null, kktBootstrap);

        public static ComponentInstallationCompletionResult PostInstallationCancelled() =>
            new(true, null, null, ComponentPostInstallationStatus.Cancelled,
                "Компоненты установлены. Последующая настройка и регистрация ТС ПИоТ отменена.");

        public static ComponentInstallationCompletionResult PostInstallationDenied() =>
            new(true, null, null, ComponentPostInstallationStatus.Denied,
                "Компоненты установлены. Последующая настройка и регистрация ТС ПИоТ недоступна без активного Service.");

        public static ComponentInstallationCompletionResult PostInstallationFailed() =>
            new(true, null, null, ComponentPostInstallationStatus.Failed,
                "Компоненты установлены. Не удалось завершить последующую настройку и регистрацию ТС ПИоТ. Подробности записаны в журнал.");
    }
}
