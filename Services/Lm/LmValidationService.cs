using ESM_Installer_SPI.Classes;
using HonestFlow.Infrastructure;
using HonestFlow.Models;
using HonestFlow.Services.Core;
using System;
using System.Threading.Tasks;

namespace HonestFlow.Services.Lm
{
    /// <summary>
    /// Реализация сервиса проверки ЛМ ЧЗ
    /// </summary>
    public class LmValidationService : ILmValidationService
    {
        private readonly ILogService _log;
        private LmModule _cachedLmModule; // Кэшируем экземпляр
        private readonly object _lockObject = new object();

        public LmValidationService(ILogService logService)
        {
            _log = logService;
        }

        /// <summary>
        /// Получить или создать экземпляр LmModule
        /// </summary>
        private LmModule GetLmModule(string expectedVersion)
        {
            lock (_lockObject)
            {
                if (_cachedLmModule == null)
                {
                    // Создаём один раз и переиспользуем
                    _cachedLmModule = new LmModule("", expectedVersion);
                    _log.LogDebug("LmModule создан и закэширован");
                }
                return _cachedLmModule;
            }
        }

        public async Task<LmStatus> GetLmStatus(string expectedVersion)
        {
            var lmModule = GetLmModule(expectedVersion);
            return await lmModule.GetStatus();
        }

        public async Task<(bool NeedInstall, string DisplayStatus)> GetLmStatusInfo(string expectedVersion)
        {
            if (string.IsNullOrEmpty(expectedVersion))
                return (false, "версия не задана");

            try
            {
                var lmModule = GetLmModule(expectedVersion);
                var status = await lmModule.GetStatus();

                if (status == null)
                    return (true, "не установлен");

                if (status.version != expectedVersion)
                    return (true, "требуется обновление");

                if (status.status == "ready")
                    return (false, "OK (активен)");
                if (status.status == "initialization")
                    return (false, "OK (инициализирован)");
                if (status.status == "not_configured")
                    return (true, "не инициализирован");

                return (true, $"статус: {status.status}");
            }
            catch (Exception ex)
            {
                _log.LogDebug($"GetLmStatusInfo ошибка: {ex.Message}");
                return (true, "ошибка проверки");
            }
        }
    }
}