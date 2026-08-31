using System;
using System.Linq;
using HonestFlow.Infrastructure.Api;

namespace HonestFlow.Infrastructure.Updates
{
    public sealed class SelfUpdateInfo
    {
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public string AssetName { get; set; }
        public string Sha256 { get; set; }
        public long? SizeBytes { get; set; }

        public static SelfUpdateInfo FromCurrentConfiguration(ApiConfigurationResponse configuration)
        {
            ApiComponentConfiguration component = configuration?.Components?.FirstOrDefault(item =>
                string.Equals(item?.Component, "HonestFlow", StringComparison.OrdinalIgnoreCase));
            return component is null ? null : new SelfUpdateInfo
            {
                Version = component.EffectiveVersion,
                AssetName = component.FileName,
                DownloadUrl = component.DownloadUrl,
                Sha256 = component.Sha256,
                SizeBytes = component.SizeBytes
            };
        }
    }
}
