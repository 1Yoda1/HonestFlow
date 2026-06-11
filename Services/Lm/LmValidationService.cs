using System;
using System.Threading.Tasks;
using HonestFlow.Models;
using HonestFlow.Services.Core;

namespace HonestFlow.Services.Lm
{
    /// <summary>
    /// Лёгкая проверка ЛМ ЧЗ для построения плана установки.
    /// Тяжёлые сценарии установки/переустановки находятся в LmModuleService.
    /// </summary>
    public class LmValidationService : ILmValidationService
    {
        private readonly ILogService _log;
        private LmModuleService _cachedLmModule;
        private string _cachedExpectedVersion;
        private readonly object _lockObject = new();

        public LmValidationService(ILogService logService)
        {
            _log = logService;
        }

        private LmModuleService GetLmModule(string expectedVersion)
        {
            lock (_lockObject)
            {
                if (_cachedLmModule == null || _cachedExpectedVersion != expectedVersion)
                {
                    _cachedLmModule = new LmModuleService(string.Empty, expectedVersion);
                    _cachedExpectedVersion = expectedVersion;
                    _log.LogDebug($"LmModuleService создан и закэширован для версии {expectedVersion}");
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

                if (status.Version != expectedVersion)
                    return (true, "требуется обновление");

                if (status.Status == "ready")
                    return (false, "OK (активен)");

                if (status.Status == "initialization")
                    return (false, "OK (инициализирован)");

                if (status.Status == "not_configured")
                    return (true, "не инициализирован");

                return (true, $"статус: {status.Status}");
            }
            catch (Exception ex)
            {
                _log.LogDebug($"GetLmStatusInfo ошибка: {ex.Message}");
                return (true, "ошибка проверки");
            }
        }
    }
}
