using System.Threading.Tasks;
using HonestFlow.Models;

namespace HonestFlow.Application.Lm
{
    public interface ILmValidationService
    {
        Task<LmStatus> GetLmStatus(string expectedVersion);
        Task<LmValidationResult> CheckLmStatus(string expectedVersion);
        Task<(bool NeedInstall, string DisplayStatus)> GetLmStatusInfo(string expectedVersion);
    }
}
