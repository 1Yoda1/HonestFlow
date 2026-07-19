using System;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.RemoteAccess
{
    public interface IRuDesktopInstaller
    {
        Task<RuDesktopInstallResult> InstallAsync(
            IProgress<RuDesktopInstallProgress> progress = null,
            CancellationToken cancellationToken = default);
    }
}
