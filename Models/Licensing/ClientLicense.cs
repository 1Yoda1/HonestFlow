using System.Collections.Generic;

namespace HonestFlow.Models.Licensing
{
    public sealed class ClientLicense
    {
        public string ClientId { get; set; }
        public bool Enabled { get; set; }
        public string MinHonestFlowVersion { get; set; }
        public int OfflineGraceHours { get; set; }
        public List<LicenseFeature> Features { get; set; } = new();
        public List<LicensedDevice> Devices { get; set; } = new();
    }
}
