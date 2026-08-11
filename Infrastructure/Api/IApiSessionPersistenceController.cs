namespace HonestFlow.Infrastructure.Api
{
    public interface IApiSessionPersistenceController
    {
        void SetPersistSession(bool persistSession);
    }
}
