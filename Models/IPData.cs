using System.Collections.Generic;

namespace HonestFlow.Models
{
    public class IPData
    {
        public string ClientId { get; set; }
        public string Name { get; set; }
        public string Password { get; set; }
        public string Token { get; set; }
        public string Inn { get; set; }
        public string Architecture { get; set; } = "x64";
        public bool HasLmDatabaseBackup { get; set; } = false;
        public RuDesktopOptions RuDesktop { get; set; } = new();
        public EngineerAccessSettings EngineerAccess { get; set; }

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

    public sealed class EngineerAccessSettings
    {
        public string Algorithm { get; set; } = "PBKDF2-SHA256";
        public int Iterations { get; set; }
        public string SaltBase64 { get; set; }
        public string PasswordHashBase64 { get; set; }
    }
}
