using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.PointStatus;

namespace HonestFlow.Infrastructure
{
    public sealed class KktDriverProbe : IKktDriverProbe
    {
        public const string X86Path = @"C:\Program Files (x86)\ATOL\Drivers10\KKT\bin\fptr10_t.exe";
        public const string X64Path = @"C:\Program Files\ATOL\Drivers10\KKT\bin\fptr10_t.exe";
        private readonly string _x86Path;
        private readonly string _x64Path;

        public KktDriverProbe()
            : this(X86Path, X64Path)
        {
        }

        public KktDriverProbe(string x86Path, string x64Path)
        {
            _x86Path = x86Path ?? throw new ArgumentNullException(nameof(x86Path));
            _x64Path = x64Path ?? throw new ArgumentNullException(nameof(x64Path));
        }

        public Task<KktDriverProbeResult> CheckAsync(string requiredArchitecture, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string architecture = KktDriverProbeResult.NormalizeArchitecture(requiredArchitecture);
            string path = architecture == "x86" ? _x86Path : _x64Path;
            try
            {
                if (!File.Exists(path))
                    return Task.FromResult(KktDriverProbeResult.NotFound(architecture));

                string version = FileVersionInfo.GetVersionInfo(path).FileVersion;
                return Task.FromResult(KktDriverProbeResult.Found(architecture, version));
            }
            catch (UnauthorizedAccessException)
            {
                return Task.FromResult(KktDriverProbeResult.Unavailable(architecture, "access_denied"));
            }
            catch (IOException)
            {
                return Task.FromResult(KktDriverProbeResult.Unavailable(architecture, "io_error"));
            }
        }
    }
}
