using HonestFlow.Models;

namespace HonestFlow.Application.Security
{
    public interface IEngineerAccessService
    {
        EngineerAccessResult CheckAccess(IPData client);
        EngineerAccessResult Unlock(IPData client, string password);
        void Lock();
    }
}
