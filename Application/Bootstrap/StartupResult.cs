using System.Collections.Generic;
using HonestFlow.Models;
using HonestFlow.Application.Auth;
using HonestFlow.Infrastructure.Api;

namespace HonestFlow.Application.Bootstrap
{
    public class StartupResult
    {
        public bool UseRemoteConfigMode { get; set; }
        public List<IPData> Ips { get; set; }
        public List<IPData> RemoteIps { get; set; }
        public VersionsData RemoteVersions { get; set; }
        public ApiConfigurationResponse CurrentApiConfiguration { get; set; }
        public IAuthService AuthService { get; set; }
        public IPData AuthorizedClient { get; set; }
        public bool SellerAuthenticationHandled { get; set; }
    }
}
