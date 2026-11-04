namespace HonestFlow.Application.Core
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

        /// <summary>Получение последних пользовательских записей; полная история хранится в файле.</summary>
        string GetUserLog();
    }
}
