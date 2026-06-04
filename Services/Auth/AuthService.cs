using ESM_Installer_SPI.Models;
using HonestFlow.Infrastructure;
using HonestFlow.Services.Core;
using System.Collections.Generic;

namespace HonestFlow.Services.Auth
{
    /// <summary>
    /// Реализация сервиса авторизации
    /// </summary>
    public class AuthService : IAuthService
    {
        private List<IPData> _ipList;
        private readonly ILogService _logService;

        public AuthService(ILogService logService)
        {
            _logService = logService;
            LoadIpList();
        }

        public void LoadIpList()
        {
            _ipList = ConfigManager.LoadIps();
        }

        public List<IPData> GetIpList()
        {
            return _ipList;
        }

        public IPData Authenticate(string password)
        {
            var ip = _ipList.Find(x => x.Password == password);
            if (ip != null)
            {
                _logService.LogDebug($"Успешная авторизация: {ip.Name}");
            }
            else
            {
                _logService.LogDebug($"Неудачная попытка авторизации с паролем: {password}");
            }
            return ip;
        }
    }
}