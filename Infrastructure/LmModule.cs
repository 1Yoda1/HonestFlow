using HonestFlow.Services.Lm;

namespace HonestFlow.Infrastructure
{
    /// <summary>
    /// Совместимость со старым кодом. Новая реализация живёт в Services/Lm/LmModuleService.
    /// После замены всех вызовов LmModule на LmModuleService этот файл можно удалить.
    /// </summary>
    [System.Obsolete("Используйте HonestFlow.Services.Lm.LmModuleService")]
    public class LmModule : LmModuleService
    {
        public LmModule(string installerPath, string expectedVersion)
            : base(installerPath, expectedVersion)
        {
        }
    }
}
