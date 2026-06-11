using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace HonestFlow.Infrastructure
{
    /// <summary>
    /// Центральный файловый логгер HonestFlow.
    /// Пишет подробные диагностические логи в %ProgramData%\HonestFlow\logs.
    /// Старые вызовы Logger.LogToFile(...) и Logger.LogException(...) сохранены для совместимости.
    /// </summary>
    public static class Logger
    {
        private static readonly object Sync = new();
        private static readonly Regex SecretRegex = new(
            @"(?i)(token|password|passwd|pwd|secret|api[_-]?key)\s*[:=]\s*([^\s;,&]+)",
            RegexOptions.Compiled);

        private static string _logFilePath;
        private static string _sessionId;
        private static bool _initialized;

        public static void Initialize()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LogsFolder);
                AppPaths.EnsureRuntimeFolders();
                CleanupOldLogs(daysToKeep: 30);

                _sessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                _logFilePath = Path.Combine(AppPaths.LogsFolder, $"{DateTime.Now:yyyy-MM-dd}.log");
                _initialized = true;

                WriteSessionHeader();
                Info("Логгер инициализирован", nameof(Logger));
            }
            catch (Exception ex)
            {
                _initialized = false;
                Debug.WriteLine($"Не удалось создать лог-файл: {ex}");
                MessageBox.Show($"Не удалось создать лог-файл: {ex.Message}", "Ошибка логирования",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public static void Info(string message, string module = null) => Write("INFO", message, module);
        public static void Success(string message, string module = null) => Write("SUCCESS", message, module);
        public static void Warning(string message, string module = null) => Write("WARNING", message, module);
        public static void Error(string message, string module = null) => Write("ERROR", message, module);
        public static void DebugLog(string message, string module = null) => Write("DEBUG", message, module);
        public static void Start(string message, string module = null) => Write("START", message, module);
        public static void End(string message, string module = null) => Write("END", message, module);

        public static IDisposable BeginOperation(string operationName, string module = null)
        {
            return new LogOperation(operationName, module);
        }

        /// <summary>
        /// Старый метод оставлен, чтобы не ломать существующий код.
        /// </summary>
        public static void LogToFile(string message, bool isError = false)
        {
            Write(isError ? "ERROR" : "INFO", message, null);
        }

        public static void LogException(string context, Exception ex)
        {
            LogException(ex, context, null);
        }

        public static void LogException(Exception ex, string context = null, string module = null)
        {
            if (ex == null)
            {
                Warning($"LogException вызван без Exception. Контекст: {context}", module);
                return;
            }

            EnsureInitializedSafe();

            try
            {
                lock (Sync)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(new string('=', 100));
                    sb.AppendLine(FormatLine("EXCEPTION", context ?? "Исключение", module));
                    sb.AppendLine($"Тип: {ex.GetType().FullName}");
                    sb.AppendLine($"Сообщение: {MaskSecrets(ex.Message)}");
                    sb.AppendLine("Стек:");
                    sb.AppendLine(MaskSecrets(ex.StackTrace ?? "<stack trace отсутствует>"));

                    if (ex.InnerException != null)
                    {
                        sb.AppendLine("InnerException:");
                        sb.AppendLine($"Тип: {ex.InnerException.GetType().FullName}");
                        sb.AppendLine($"Сообщение: {MaskSecrets(ex.InnerException.Message)}");
                        sb.AppendLine(MaskSecrets(ex.InnerException.StackTrace ?? "<stack trace отсутствует>"));
                    }

                    sb.AppendLine(new string('=', 100));
                    File.AppendAllText(_logFilePath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch (Exception loggerEx)
            {
                Debug.WriteLine($"Ошибка логгера: {loggerEx.Message}");
            }
        }

        public static string GetLogPath() => _logFilePath;
        public static string GetLogsFolder() => AppPaths.LogsFolder;
        public static bool IsInitialized => _initialized;

        private static void Write(string level, string message, string module)
        {
            EnsureInitializedSafe();

            if (!_initialized)
                return;

            try
            {
                lock (Sync)
                {
                    File.AppendAllText(_logFilePath, FormatLine(level, message, module) + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка логгера: {ex.Message}");
            }
        }

        private static string FormatLine(string level, string message, string module)
        {
            string safeMessage = MaskSecrets(message ?? string.Empty).Replace(Environment.NewLine, " | ");
            string safeModule = string.IsNullOrWhiteSpace(module) ? "General" : module.Trim();
            int threadId = Thread.CurrentThread.ManagedThreadId;
            return $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level,-9}] [{safeModule}] [T:{threadId:00}] {safeMessage}";
        }

        private static void WriteSessionHeader()
        {
            lock (Sync)
            {
                var assembly = Assembly.GetExecutingAssembly();
                string version = assembly.GetName().Version?.ToString() ?? "unknown";

                var sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine(new string('=', 100));
                sb.AppendLine($"HONESTFLOW SESSION START: {_sessionId}");
                sb.AppendLine(new string('-', 100));
                sb.AppendLine($"Время запуска:      {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                sb.AppendLine($"Версия приложения:  {version}");
                sb.AppendLine($"ОС:                 {Environment.OSVersion}");
                sb.AppendLine($"64-bit OS:          {Environment.Is64BitOperatingSystem}");
                sb.AppendLine($"64-bit process:     {Environment.Is64BitProcess}");
                sb.AppendLine($"MachineName:        {Environment.MachineName}");
                sb.AppendLine($"UserName:           {Environment.UserName}");
                sb.AppendLine($"Admin:              {IsAdministratorSafe()}");
                sb.AppendLine($"BaseFolder:         {AppPaths.BaseFolder}");
                sb.AppendLine($"ProgramDataFolder:  {AppPaths.ProgramDataFolder}");
                sb.AppendLine($"LogsFolder:         {AppPaths.LogsFolder}");
                sb.AppendLine($"LogFile:            {_logFilePath}");
                sb.AppendLine(new string('=', 100));
                File.AppendAllText(_logFilePath, sb.ToString(), Encoding.UTF8);
            }
        }

        private static void EnsureInitializedSafe()
        {
            if (_initialized)
                return;

            try
            {
                Directory.CreateDirectory(AppPaths.LogsFolder);
                _sessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                _logFilePath ??= Path.Combine(AppPaths.LogsFolder, $"{DateTime.Now:yyyy-MM-dd}.log");
                _initialized = true;
                WriteSessionHeader();
            }
            catch
            {
                _initialized = false;
            }
        }

        private static void CleanupOldLogs(int daysToKeep)
        {
            try
            {
                if (!Directory.Exists(AppPaths.LogsFolder))
                    return;

                var border = DateTime.Now.AddDays(-daysToKeep);
                foreach (var file in Directory.GetFiles(AppPaths.LogsFolder, "*.log"))
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTime < border)
                        info.Delete();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Не удалось очистить старые логи: {ex.Message}");
            }
        }

        private static string MaskSecrets(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return SecretRegex.Replace(text, "$1=***");
        }

        private static bool IsAdministratorSafe()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private sealed class LogOperation : IDisposable
        {
            private readonly Stopwatch _stopwatch;
            private readonly string _operationName;
            private readonly string _module;
            private bool _disposed;

            public LogOperation(string operationName, string module)
            {
                _operationName = string.IsNullOrWhiteSpace(operationName) ? "Операция" : operationName;
                _module = module;
                _stopwatch = Stopwatch.StartNew();
                Start(_operationName, _module);
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _stopwatch.Stop();
                End($"{_operationName} завершено за {_stopwatch.Elapsed.TotalSeconds:F2} сек", _module);
            }
        }
    }
}
