using HonestFlow.Models;

namespace HonestFlow.Services.Installation
{
    /// <summary>
    /// Интерфейс сервиса проверки версий компонентов
    /// </summary>
    public interface IVersionCheckService
    {
        string GetAtolDriverInfo();
        string GetEsmVersion();
        string GetControllerVersion();
        /// <summary>Проверить нужна ли установка драйвера АТОЛ</summary>
        bool NeedAtolInstall(IPData selectedIP, string expectedVersion);

        /// <summary>Проверить нужна ли установка ЕСМ</summary>
        bool NeedEsmInstall(string expectedVersion);

        /// <summary>Проверить нужна ли установка Контроллера</summary>
        bool NeedControllerInstall(string expectedVersion);
    }
}