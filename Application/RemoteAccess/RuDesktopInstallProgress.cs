namespace HonestFlow.Application.RemoteAccess
{
    public sealed class RuDesktopInstallProgress
    {
        public RuDesktopInstallProgress(int percent, string message)
        {
            Percent = percent;
            Message = message;
        }

        public int Percent { get; }
        public string Message { get; }
    }
}
