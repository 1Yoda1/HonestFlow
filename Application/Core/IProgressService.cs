namespace HonestFlow.Application.Core
{
    /// <summary>
    /// Интерфейс сервиса прогресса
    /// </summary>
    public interface IProgressService
    {
        /// <summary>Обновить прогресс и статус</summary>
        void SetProgress(int percent, string stepName);
    }
}
