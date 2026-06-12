using System;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Models;
using HonestFlow.Services.Core;

namespace HonestFlow.Services.Lm
{
    /// <summary>
    /// Лёгкий сервис статуса ЛМ ЧЗ.
    /// Важно: сервис не знает ожидаемую версию ЛМ и не должен требовать installerPath.
    /// </summary>
    public class LmStatusService : ILmStatusService
    {
        private readonly ILogService _log;
        private readonly LmApiClient _apiClient;

        public LmStatusService(ILogService logService)
        {
            _log = logService;
            _apiClient = new LmApiClient(true);
        }

        public async Task<bool> IsApiAvailable()
        {
            try
            {
                return await _apiClient.IsApiAvailable();
            }
            catch (Exception ex)
            {
                _log.LogDebug($"LmStatusService.IsApiAvailable ошибка: {ex.Message}");
                return false;
            }
        }

        public async Task<LmStatus> GetStatus()
        {
            try
            {
                var response = await _apiClient.GetStatus();
                return response.IsSuccess ? response.Data : null;
            }
            catch (Exception ex)
            {
                _log.LogDebug($"LmStatusService.GetStatus ошибка: {ex.Message}");
                return null;
            }
        }
    }
}
