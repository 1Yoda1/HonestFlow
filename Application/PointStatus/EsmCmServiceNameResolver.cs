using System;
using System.IO;
using System.Linq;

namespace HonestFlow.Application.PointStatus
{
    public sealed class EsmCmServiceNameResolver
    {
        public const string ServicePrefix = "esm-cm-";
        public const string DefaultEsmFolder = @"C:\ProgramData\ESP\ESM";

        private readonly string _esmFolder;

        public EsmCmServiceNameResolver(string esmFolder = DefaultEsmFolder)
        {
            _esmFolder = esmFolder ?? throw new ArgumentNullException(nameof(esmFolder));
        }

        public string ResolveFromLogs()
        {
            try
            {
                if (!Directory.Exists(_esmFolder))
                    return null;

                return Directory
                    .EnumerateFiles(_esmFolder, ServicePrefix + "*.log", SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Select(file => Path.GetFileNameWithoutExtension(file.Name))
                    .FirstOrDefault(IsValidServiceName);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        public static bool IsValidServiceName(string serviceName) =>
            !string.IsNullOrWhiteSpace(serviceName) &&
            serviceName.StartsWith(ServicePrefix, StringComparison.OrdinalIgnoreCase) &&
            serviceName.Length > ServicePrefix.Length;
    }
}
