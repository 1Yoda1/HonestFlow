namespace HonestFlow.Services.Core
{
    /// <summary>
    /// Интерфейс сервиса прогресса
    /// </summary>
    public interface IProgressService
    {
        /// <summary>Обновить прогресс и статус</summary>
        void SetProgress(int percent, string stepName);

        /// <summary>Показать/скрыть прогресс-бар</summary>
        void SetProgressVisible(bool visible);
    }
}