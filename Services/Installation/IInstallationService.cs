using ESM_Installer_SPI.Models;
using System.Threading.Tasks;

namespace HonestFlow.Services.Installation
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