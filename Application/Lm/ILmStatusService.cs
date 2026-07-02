using System.Threading.Tasks;
using HonestFlow.Models;

namespace HonestFlow.Application.Lm
{
    /// <summary>
    /// Диагностика уже установленного ЛМ ЧЗ без зависимости от ожидаемой версии установщика.
    /// Используется машинной диагностикой и UI-статусом.
    /// </summary>
    public interface ILmStatusService
    {
        Task<bool> IsApiAvailable();
        Task<LmStatus> GetStatus();
    }
}
