using System.Collections.Generic;

namespace HonestFlow.Infrastructure.Configuration
{
    public sealed class InstallerCacheLocationState
    {
        public int SchemaVersion { get; set; } = 1;
        public List<string> Locations { get; set; } = new();
    }
}
