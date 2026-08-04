namespace HonestFlow.Infrastructure.Updates
{
    public sealed class SelfUpdateManifest
    {
        public const int CurrentSchemaVersion = 1;
        public const string ExpectedAssetName = "HonestFlow.exe";

        public int SchemaVersion { get; set; }
        public string Version { get; set; }
        public string AssetName { get; set; }
        public long Size { get; set; }
        public string Sha256 { get; set; }
    }
}
