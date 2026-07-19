using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Configuration
{
    public sealed class InstallerCacheLocationStore
    {
        private readonly string _stateFile;

        public InstallerCacheLocationStore(string stateFile = null)
        {
            _stateFile = string.IsNullOrWhiteSpace(stateFile)
                ? AppPaths.LegacyInstallerCacheLocationsFile
                : Path.GetFullPath(stateFile);
        }

        public int RegisterLocations(IEnumerable<string> candidateFolders)
        {
            var locations = ReadLocations().ToList();
            int added = 0;
            foreach (string candidate in candidateFolders ?? Array.Empty<string>())
            {
                if (!TryNormalizeNonEmptyDirectory(candidate, out string normalized) ||
                    locations.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                locations.Add(normalized);
                added++;
            }

            if (added == 0)
                return 0;

            return TryWrite(locations) ? added : 0;
        }

        public IReadOnlyList<string> ReadLocations()
        {
            try
            {
                if (!File.Exists(_stateFile))
                    return Array.Empty<string>();

                InstallerCacheLocationState state =
                    JsonConvert.DeserializeObject<InstallerCacheLocationState>(
                        File.ReadAllText(_stateFile));
                if (state?.SchemaVersion != 1 || state.Locations == null)
                    return Array.Empty<string>();

                return state.Locations
                    .Where(path => TryNormalizePath(path, out _))
                    .Select(Path.GetFullPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private bool TryWrite(IEnumerable<string> locations)
        {
            string temporaryPath = null;
            try
            {
                string directory = Path.GetDirectoryName(_stateFile);
                Directory.CreateDirectory(directory);
                temporaryPath = _stateFile + ".tmp-" + Guid.NewGuid().ToString("N");
                var state = new InstallerCacheLocationState
                {
                    Locations = locations
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };
                File.WriteAllText(
                    temporaryPath,
                    JsonConvert.SerializeObject(state, Formatting.Indented));
                File.Move(temporaryPath, _stateFile, overwrite: true);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    if (!string.IsNullOrEmpty(temporaryPath) && File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                    // Сбой очистки временного файла не должен мешать запуску HonestFlow.
                }
            }
        }

        private static bool TryNormalizeNonEmptyDirectory(
            string path,
            out string normalized)
        {
            normalized = null;
            if (!TryNormalizePath(path, out normalized) || !Directory.Exists(normalized))
                return false;

            try
            {
                return Directory.EnumerateFiles(normalized, "*", SearchOption.TopDirectoryOnly)
                    .Any(file => !IsTemporaryFile(Path.GetFileName(file)));
            }
            catch
            {
                return false;
            }
        }

        private static bool TryNormalizePath(string path, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                normalized = Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return Path.IsPathRooted(normalized);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTemporaryFile(string fileName) =>
            fileName.StartsWith(".", StringComparison.Ordinal) ||
            fileName.EndsWith(".download", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
    }
}
