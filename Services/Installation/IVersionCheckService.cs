using HonestFlow.Models;

namespace HonestFlow.Services.Installation
{
    public interface IVersionCheckService
    {
        bool NeedAtolInstall(IPData selectedIP, string expectedVersion);
        bool NeedEsmInstall(string expectedVersion);
        bool NeedControllerInstall(string expectedVersion);

        string GetAtolDriverInfo();
        string GetEsmVersion();
        string GetControllerVersion();
    }
}
