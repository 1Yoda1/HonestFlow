namespace HonestFlow.Application.RemoteAccess
{
    public sealed class RuDesktopPackage
    {
        public string Version { get; init; }
        public string FileName { get; init; }
        public long Size { get; init; }
        public string Sha256 { get; init; }
        public string SignerThumbprint { get; init; }
    }
}
