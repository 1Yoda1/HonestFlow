using HonestFlow.Models;
using System.Collections.Generic;

namespace HonestFlow.Application.Auth
{
    /// <summary>
    /// Интерфейс сервиса авторизации
    /// </summary>
    public interface IAuthService
    {
        /// <summary>Загрузить список ИП из файла</summary>
        void LoadIpList();

        /// <summary>Авторизация по паролю</summary>
        IPData Authenticate(string password);
    }
}
