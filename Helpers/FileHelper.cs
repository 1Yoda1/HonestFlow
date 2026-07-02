using HonestFlow.Application.Core;
using System.IO;
using System.Linq;

namespace HonestFlow.Helpers
{
    /// <summary>
    /// Вспомогательный класс для работы с файлами
    /// </summary>
    public static class FileHelper
    {
        /// <summary>
        /// Поиск установщика драйвера АТОЛ по разрядности
        /// </summary>
        public static string GetAtolInstallerByArchitecture(string folder, string architecture, ILogService log)
        {
            if (architecture?.ToLower() == "x86")
            {
                var specific = Directory.GetFiles(folder, "KKT10-*x86*.exe").Concat(
                                Directory.GetFiles(folder, "KKT10-*32*.exe")).ToArray();
                if (specific.Length > 0)
                    return specific[0];
            }
            else
            {
                var specific = Directory.GetFiles(folder, "KKT10-*x64*.exe").Concat(
                                Directory.GetFiles(folder, "KKT10-*64*.exe")).ToArray();
                if (specific.Length > 0)
                    return specific[0];
            }

            var any = Directory.GetFiles(folder, "KKT10-*.exe");
            if (any.Length > 0)
            {
                log?.LogDebug($"⚠️ Не найден драйвер для {architecture}, взят первый попавшийся");
                return any[0];
            }

            return null;
        }
    }
}