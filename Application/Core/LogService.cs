using System;
using System.Text;
using HonestFlow.Infrastructure;


namespace HonestFlow.Application.Core
{
    /// <summary>
    /// Реализация сервиса логирования
    /// </summary>
    public class LogService : ILogService
    {
        private readonly StringBuilder _userLog = new();

        public void LogUser(string message, bool isError = false)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string formatted = $"[{timestamp}] {(isError ? "❌" : "✓")} {message}";
            _userLog.AppendLine(formatted);
            Logger.LogToFile(message, isError);
        }

        public void LogDebug(string message)
        {
            Logger.LogToFile($"[DEBUG] {message}");
        }

        public string GetUserLog()
        {
            return _userLog.ToString();
        }
    }
}