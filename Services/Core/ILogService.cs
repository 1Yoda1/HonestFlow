namespace HonestFlow.Services.Core
{
    /// <summary>
    /// Интерфейс сервиса логирования
    /// </summary>
    public interface ILogService
    {
        /// <summary>Запись в пользовательский лог (окно "Подробнее")</summary>
        void LogUser(string message, bool isError = false);

        /// <summary>Запись отладочного сообщения (только в файл)</summary>
        void LogDebug(string message);

        /// <summary>Получение всего пользовательского лога</summary>
        string GetUserLog();
    }
}