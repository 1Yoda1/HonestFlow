namespace HonestFlow.Application.Installation
{
    public sealed class InstallationOptions
    {
        public static InstallationOptions Default { get; } = new();
        public bool SkipLmStack { get; init; }
    }
}
