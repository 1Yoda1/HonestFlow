using System;
using System.Linq;
using HonestFlow.Infrastructure.Api;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Updates
{
    public sealed class SelfUpdateInfo
    {
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        [JsonProperty("fileName")]
        public string AssetName { get; set; }
        public string Architecture { get; set; }
        public string Sha256 { get; set; }
        public long? SizeBytes { get; set; }
        public string ReleaseKeyId { get; set; }
        public string ManifestBase64 { get; set; }
        public string ReleaseSignatureBase64 { get; set; }

        public static SelfUpdateInfo FromCurrentConfiguration(ApiConfigurationResponse configuration)
        {
            ApiComponentConfiguration component = configuration?.Components?.FirstOrDefault(item =>
                string.Equals(item?.Component, "HonestFlow", StringComparison.OrdinalIgnoreCase));
            return component is null ? null : new SelfUpdateInfo
            {
                Version = component.EffectiveVersion,
                AssetName = component.FileName,
                Architecture = component.Architecture,
                DownloadUrl = component.DownloadUrl,
                Sha256 = component.Sha256,
                SizeBytes = component.SizeBytes,
                ReleaseKeyId = component.ReleaseKeyId,
                ManifestBase64 = component.ManifestBase64,
                ReleaseSignatureBase64 = component.ReleaseSignatureBase64
            };
        }
    }
}
