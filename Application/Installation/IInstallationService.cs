using System.Threading.Tasks;
using HonestFlow.Models;


namespace HonestFlow.Application.Installation
{
    /// <summary>
    /// Интерфейс сервиса установки
    /// </summary>
    public interface IInstallationService
    {
        /// <summary>Проверить ЛМ ЧЗ и выполнить установку при необходимости</summary>
        Task<bool> CheckLmAndInstall(IPData selectedIP);
    }
}