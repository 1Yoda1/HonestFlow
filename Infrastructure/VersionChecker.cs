using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace HonestFlow.Infrastructure
{
    public static class VersionChecker
    {
        public static string GetAtolDriverInfo()
        {
            var foundVersions = new List<string>();
            string[] filePaths =
            {
                @"C:\Program Files (x86)\ATOL\Drivers10\KKT\bin\fptr10_t.exe",
                @"C:\Program Files\ATOL\Drivers10\KKT\bin\fptr10_t.exe"
            };

            foreach (var path in filePaths)
            {
                if (File.Exists(path))
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(path);
                    string arch = path.Contains("Program Files (x86)") ? "32-bit" : "64-bit";
                    string version = versionInfo.FileVersion ?? "версия не определена";
                    string entry = $"{version} ({arch})";
                    if (!foundVersions.Contains(entry))
                        foundVersions.Add(entry);
                }
            }

            if (foundVersions.Count == 0) return "не установлен";
            if (foundVersions.Count == 1) return foundVersions[0];
            return string.Join(" и ", foundVersions);
        }

        public static string GetEsmVersion()
        {
            string[] possiblePaths =
            {
                @"C:\Program Files\ESP\ESM\Uninstall.exe",
                @"C:\Program Files (x86)\ESP\ESM\Uninstall.exe"
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(path);
                    return versionInfo.FileVersion ?? "версия не определена";
                }
            }
            return "не установлен";
        }

        public static string GetControllerVersion()
        {
            string[] possiblePaths =
            {
                @"C:\Program Files\ESP\LMController\Uninstall.exe",
                @"C:\Program Files (x86)\ESP\LMController\Uninstall.exe"
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(path);
                    string arch = path.Contains("Program Files (x86)") ? "32-bit" : "64-bit";
                    return $"{versionInfo.FileVersion} ({arch})";
                }
            }
            return "не установлен";
        }
    }
}