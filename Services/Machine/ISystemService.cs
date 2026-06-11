using HonestFlow.Models;
using System.Threading.Tasks;

namespace HonestFlow.Services.Machine
{
    /// <summary>
    /// Интерфейс системного сервиса
    /// Отвечает за управление службой Regime, проверку API и получение системной информации
    /// </summary>
    public interface ISystemService
    {
        /// <summary>Управление службой Regime (stop/start/restart)</summary>
        Task<bool> ManageService(string action);

        /// <summary>Получить статус службы Regime</summary>
        Task<string> GetServiceStatus();

        /// <summary>Проверить доступность API ЛМ ЧЗ</summary>
        Task<bool> IsApiAvailable();

        /// <summary>Получить статус API ЛМ ЧЗ</summary>
        Task<LmStatus> GetApiStatus();

        /// <summary>Получить системную информацию для отображения</summary>
        string GetSystemInfo();
    }
}