using System.Collections.Generic;
using System.IO;
using HonestFlow.Models;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure
{
    /// <summary>
    /// Локальные конфиги рядом с exe: ips.json и versions.json.
    /// </summary>
    public class LocalConfigRepository
    {
        public List<IPData> LoadIps()
        {
            if (!File.Exists(AppPaths.LocalIpsFile))
                return new List<IPData>();

            var encryptedJson = File.ReadAllText(AppPaths.LocalIpsFile);
            var json = ObfuscationService.Deobfuscate(encryptedJson);
            return JsonConvert.DeserializeObject<List<IPData>>(json) ?? new List<IPData>();
        }

        public void SaveIps(List<IPData> ips)
        {
            var json = JsonConvert.SerializeObject(ips, Formatting.Indented);
            var encryptedJson = ObfuscationService.Obfuscate(json);
            File.WriteAllText(AppPaths.LocalIpsFile, encryptedJson);
        }

        public VersionsData LoadVersions()
        {
            if (!File.Exists(AppPaths.LocalVersionsFile))
                return new VersionsData();

            var json = File.ReadAllText(AppPaths.LocalVersionsFile);
            return JsonConvert.DeserializeObject<VersionsData>(json) ?? new VersionsData();
        }

        public void SaveVersions(VersionsData versions)
        {
            var json = JsonConvert.SerializeObject(versions, Formatting.Indented);
            File.WriteAllText(AppPaths.LocalVersionsFile, json);
        }
    }
}
