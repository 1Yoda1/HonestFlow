using System;
using System.IO;
using HonestFlow.Models;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Configuration
{
    public sealed class VersionConfigurationCache
    {
        private readonly string _path;

        public VersionConfigurationCache(string path = null)
        {
            _path = string.IsNullOrWhiteSpace(path) ? AppPaths.CachedVersionsFile : path;
        }

        public void Save(VersionsData versions)
        {
            if (versions == null)
                return;

            try
            {
                string directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                string temporaryPath = _path + ".tmp";
                File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(versions, Formatting.Indented));
                File.Move(temporaryPath, _path, true);
            }
            catch (Exception ex)
            {
                Logger.Warning(
                    $"Event=VersionConfigurationCacheWriteFailed ErrorType={ex.GetType().Name}",
                    nameof(VersionConfigurationCache));
            }
        }

        public VersionsData Load()
        {
            try
            {
                if (!File.Exists(_path))
                    return null;

                return JsonConvert.DeserializeObject<VersionsData>(File.ReadAllText(_path));
            }
            catch (Exception ex)
            {
                Logger.Warning(
                    $"Event=VersionConfigurationCacheReadFailed ErrorType={ex.GetType().Name}",
                    nameof(VersionConfigurationCache));
                return null;
            }
        }
    }
}
