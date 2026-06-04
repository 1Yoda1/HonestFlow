using HonestFlow.Models;
using System.Threading.Tasks;

namespace HonestFlow.Services.Lm
{
    /// <summary>
    /// Интерфейс сервиса проверки ЛМ ЧЗ
    /// </summary>
    public interface ILmValidationService
    {
        /// <summary>Получить статус ЛМ ЧЗ</summary>
        Task<LmStatus> GetLmStatus(string expectedVersion);

        /// <summary>Получить информацию о статусе с вердиктом об установке</summary>
        Task<(bool NeedInstall, string DisplayStatus)> GetLmStatusInfo(string expectedVersion);
    }
}