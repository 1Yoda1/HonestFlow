namespace HonestFlow.Application.ServiceConnection;

public enum ServiceConnectionState
{
    Disconnected,
    Connecting,
    AuthenticationRequired,
    RegistrationRequired,
    RegistrationPending,
    RegistrationRejected,
    EntitlementChecking,
    Active,
    NotEntitled,
    Expired,
    Disabled,
    UpdateRequired,
    Unavailable
}
