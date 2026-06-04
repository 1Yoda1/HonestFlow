using HonestFlow.Models;
using System.Collections.Generic;

namespace HonestFlow.Services.Auth
{
    /// <summary>
    /// Интерфейс сервиса авторизации
    /// </summary>
    public interface IAuthService
    {
        /// <summary>Загрузить список ИП из файла</summary>
        void LoadIpList();

        /// <summary>Получить список ИП</summary>
        List<IPData> GetIpList();

        /// <summary>Авторизация по паролю</summary>
        IPData Authenticate(string password);
    }
}