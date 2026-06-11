using System;

namespace HonestFlow.Infrastructure
{
    /// <summary>
    /// Детальный результат запуска внешнего процесса.
    /// Старые методы ProcessRunner.Run/RunAsync оставлены для совместимости.
    /// </summary>
    public class ProcessExecutionResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; }
        public string StandardError { get; set; }
        public TimeSpan Duration { get; set; }
        public bool TimedOut { get; set; }
        public Exception Exception { get; set; }
        public bool IsSuccess => !TimedOut && Exception == null && ExitCode == 0;
    }
}
