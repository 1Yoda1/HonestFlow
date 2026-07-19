using System;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.Prerequisites
{
    public interface IDotNetDesktopRuntimeInstaller
    {
        Task<DotNetRuntimeInstallResult> EnsureInstalledAsync(
            IProgress<DotNetRuntimeInstallProgress> progress = null,
            CancellationToken cancellationToken = default);
    }
}
