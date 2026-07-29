using System.Collections.Generic;
using System.IO;
using HonestFlow.Models;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Configuration
{
    /// <summary>
    /// Локальные конфиги рядом с exe: ips.json и versions.json.
    /// </summary>
    public class LocalConfigRepository
    {
        public List<IPData> LoadIps()
        {
            try
            {
                if (!File.Exists(AppPaths.LocalIpsFile))
                    return new List<IPData>();

                var encryptedJson = File.ReadAllText(AppPaths.LocalIpsFile);
                var json = ObfuscationService.Deobfuscate(encryptedJson);
                return JsonConvert.DeserializeObject<List<IPData>>(json) ?? new List<IPData>();
            }
            catch (System.Exception ex)
            {
                Logger.LogToFile($"Local IP config loading error: {ex.Message}", true);
                return new List<IPData>();
            }
        }

        public VersionsData LoadVersions()
        {
            try
            {
                if (!File.Exists(AppPaths.LocalVersionsFile))
                    return new VersionsData();

                var json = File.ReadAllText(AppPaths.LocalVersionsFile);
                return JsonConvert.DeserializeObject<VersionsData>(json) ?? new VersionsData();
            }
            catch (System.Exception ex)
            {
                Logger.LogToFile($"Local versions config loading error: {ex.Message}", true);
                return new VersionsData();
            }
        }
    }
}
