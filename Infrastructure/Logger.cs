using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace HonestFlow.Infrastructure
{
    /// <summary>
    /// Логирование в файл. Инициализируется при запуске программы.
    /// </summary>
    public static class Logger
    {
        private static string _logFilePath;
        private static bool _initialized = false;

        /// <summary>
        /// Инициализация логгера. Создаёт папку logs и файл с датой/временем.
        /// </summary>
        public static void Initialize()
        {
            try
            {
                string logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logFolder))
                    Directory.CreateDirectory(logFolder);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _logFilePath = Path.Combine(logFolder, $"install_{timestamp}.txt");

                File.WriteAllText(_logFilePath, $"=== УСТАНОВЩИК ТС ПИоТ ===\n");
                File.AppendAllText(_logFilePath, $"Время запуска: {DateTime.Now}\n");
                File.AppendAllText(_logFilePath, $"Версия ОС: {Environment.OSVersion}\n");
                File.AppendAllText(_logFilePath, $"Пользователь: {Environment.UserName}\n");
                File.AppendAllText(_logFilePath, $"Папка программы: {AppDomain.CurrentDomain.BaseDirectory}\n");
                File.AppendAllText(_logFilePath, $"=================================\n\n");

                _initialized = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось создать лог-файл: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Запись сообщения в лог-файл
        /// </summary>
        public static void LogToFile(string message, bool isError = false)
        {
            if (!_initialized) return;

            try
            {
                string line = $"[{DateTime.Now:HH:mm:ss}] {(isError ? "[ERROR] " : "[INFO]  ")}{message}";
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка логгера: {ex.Message}");
            }
        }

        /// <summary>
        /// Запись исключения в лог
        /// </summary>
        public static void LogException(string context, Exception ex)
        {
            if (!_initialized) return;

            try
            {
                File.AppendAllText(_logFilePath, $"[{DateTime.Now:HH:mm:ss}] [EXCEPTION] {context}\n");
                File.AppendAllText(_logFilePath, $"    Тип: {ex.GetType().Name}\n");
                File.AppendAllText(_logFilePath, $"    Сообщение: {ex.Message}\n");
                File.AppendAllText(_logFilePath, $"    Стек: {ex.StackTrace}\n\n");
            }
            catch { }
        }

        /// <summary>
        /// Получить путь к текущему лог-файлу
        /// </summary>
        public static string GetLogPath()
        {
            return _logFilePath;
        }
    }
}