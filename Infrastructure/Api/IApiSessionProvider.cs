namespace HonestFlow.Infrastructure.Api
{
    public interface IApiSessionProvider
    {
        IApiSessionService ApiSessionService { get; }
    }
}
