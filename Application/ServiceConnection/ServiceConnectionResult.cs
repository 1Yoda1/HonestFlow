namespace HonestFlow.Application.ServiceConnection;

public sealed class ServiceConnectionResult
{
    public ServiceConnectionResult(ServiceConnectionState state, string message, ServiceRuntimeContext? context = null)
    {
        State = state;
        Message = message;
        Context = context;
    }

    public ServiceConnectionState State { get; }
    public string Message { get; }
    public ServiceRuntimeContext? Context { get; }
    public bool IsActive => State == ServiceConnectionState.Active && Context is not null;
}
