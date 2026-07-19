namespace HonestFlow.Application.Prerequisites
{
    public sealed class DotNetRuntimeInstallProgress
    {
        public DotNetRuntimeInstallProgress(int percent, string message)
        {
            Percent = percent;
            Message = message;
        }

        public int Percent { get; }
        public string Message { get; }
    }
}
