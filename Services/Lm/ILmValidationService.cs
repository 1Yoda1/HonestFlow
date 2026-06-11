using System.Threading.Tasks;
using HonestFlow.Models;

namespace HonestFlow.Services.Lm
{
    public interface ILmValidationService
    {
        Task<LmStatus> GetLmStatus(string expectedVersion);
        Task<(bool NeedInstall, string DisplayStatus)> GetLmStatusInfo(string expectedVersion);
    }
}
