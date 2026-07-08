using System.Collections.Generic;

namespace HonestFlow.Models
{
    public class IPData
    {
        public string Name { get; set; }
        public string Password { get; set; }
        public string Token { get; set; }
        public string Inn { get; set; }
        public string Architecture { get; set; } = "x64";
        public bool HasLmDatabaseBackup { get; set; } = false;
        public RuDesktopOptions RuDesktop { get; set; } = new();

        public List<string> Tags { get; set; } = new();
        public VersionsData Versions { get; set; } = new();

        public bool HasTag(string tag)
        {
            return Tags != null && Tags.Exists(x =>
                string.Equals(x, tag, System.StringComparison.OrdinalIgnoreCase));
        }
    }

    public class RuDesktopOptions
    {
        public bool Enabled { get; set; } = false;
        public bool AutoOfferPasswordSetup { get; set; } = false;
        public bool SuppressPasswordSetupPrompt { get; set; } = false;
        public string Password { get; set; }
    }
}
