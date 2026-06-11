using System.Collections.Generic;
using HonestFlow.Models;
using HonestFlow.Services.Auth;
using HonestFlow.Services.Installation;

namespace HonestFlow.Application.Bootstrap
{
    public class StartupResult
    {
        public bool UseGitHubMode { get; set; }
        public List<IPData> GitHubIps { get; set; }
        public VersionsData GitHubVersions { get; set; }
        public IAuthService AuthService { get; set; }
        public IInstallationService InstallationService { get; set; }
    }
}
