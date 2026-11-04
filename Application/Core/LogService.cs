using System;
using System.Collections.Generic;
using HonestFlow.Infrastructure;


namespace HonestFlow.Application.Core
{
    /// <summary>
    /// Реализация сервиса логирования
    /// </summary>
    public class LogService : ILogService
    {
        public const int MaxUserLogCharacters = 128 * 1024;
        private readonly object _sync = new();
        private readonly Queue<string> _userLog = new();
        private int _userLogCharacters;

        public void LogUser(string message, bool isError = false)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string formatted = $"[{timestamp}] {(isError ? "❌" : "✓")} {message}";
            string line = formatted + Environment.NewLine;
            if (line.Length > MaxUserLogCharacters)
                line = line.Substring(line.Length - MaxUserLogCharacters);
            lock (_sync)
            {
                while (_userLogCharacters + line.Length > MaxUserLogCharacters)
                    _userLogCharacters -= _userLog.Dequeue().Length;
                _userLog.Enqueue(line);
                _userLogCharacters += line.Length;
            }
            Logger.LogToFile(message, isError);
        }

        public void LogDebug(string message)
        {
            Logger.LogToFile($"[DEBUG] {message}");
        }

        public string GetUserLog()
        {
            lock (_sync)
                return string.Concat(_userLog);
        }
    }
}
