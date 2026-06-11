using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace HonestFlow.Infrastructure
{
    /// <summary>
    /// Низкоуровневый файловый логгер.
    /// Логи пишутся в %ProgramData%\HonestFlow\logs, чтобы не зависеть от прав записи рядом с exe.
    /// </summary>
    public static class Logger
    {
        private static string _logFilePath;
        private static bool _initialized;
        private static readonly object Sync = new();

        public static void Initialize()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LogsFolder);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _logFilePath = Path.Combine(AppPaths.LogsFolder, $"install_{timestamp}.txt");

                File.WriteAllText(_logFilePath, $"=== УСТАНОВЩИК ТС ПИоТ ==={Environment.NewLine}");
                File.AppendAllText(_logFilePath, $"Время запуска: {DateTime.Now}{Environment.NewLine}");
                File.AppendAllText(_logFilePath, $"Версия ОС: {Environment.OSVersion}{Environment.NewLine}");
                File.AppendAllText(_logFilePath, $"Пользователь: {Environment.UserName}{Environment.NewLine}");
                File.AppendAllText(_logFilePath, $"Папка программы: {AppPaths.BaseFolder}{Environment.NewLine}");
                File.AppendAllText(_logFilePath, $"Папка логов: {AppPaths.LogsFolder}{Environment.NewLine}");
                File.AppendAllText(_logFilePath, $"================================={Environment.NewLine}{Environment.NewLine}");

                _initialized = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось создать лог-файл: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public static void LogToFile(string message, bool isError = false)
        {
            if (!_initialized) return;

            try
            {
                lock (Sync)
                {
                    string line = $"[{DateTime.Now:HH:mm:ss}] {(isError ? "[ERROR] " : "[INFO]  ")}{message}";
                    File.AppendAllText(_logFilePath, line + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка логгера: {ex.Message}");
            }
        }

        public static void LogException(string context, Exception ex)
        {
            if (!_initialized) return;

            try
            {
                lock (Sync)
                {
                    File.AppendAllText(_logFilePath, $"[{DateTime.Now:HH:mm:ss}] [EXCEPTION] {context}{Environment.NewLine}");
                    File.AppendAllText(_logFilePath, $"    Тип: {ex.GetType().Name}{Environment.NewLine}");
                    File.AppendAllText(_logFilePath, $"    Сообщение: {ex.Message}{Environment.NewLine}");
                    File.AppendAllText(_logFilePath, $"    Стек: {ex.StackTrace}{Environment.NewLine}{Environment.NewLine}");
                }
            }
            catch { }
        }

        public static string GetLogPath() => _logFilePath;
        public static string GetLogsFolder() => AppPaths.LogsFolder;
    }
}
