using System.Collections.Generic;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Models;
using HonestFlow.Services.Core;

namespace HonestFlow.Services.Auth
{
    public class AuthService : IAuthService
    {
        private List<IPData> _ipList;
        private readonly ILogService _logService;

        public AuthService(ILogService logService)
        {
            _logService = logService;
            LoadIpList();
        }

        public AuthService(List<IPData> ips, ILogService logService)
        {
            _ipList = ips;
            _logService = logService;
            _logService.LogDebug($"Loaded {_ipList.Count} IP entries from Yandex Disk");
        }

        public void LoadIpList()
        {
            _ipList = ConfigManager.LoadIps();
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
                _logService.LogDebug("Неудачная попытка авторизации");
            }
            return ip;
        }
    }
}
