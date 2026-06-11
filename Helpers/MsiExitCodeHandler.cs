using System;

namespace HonestFlow.Infrastructure
{
    public static class MsiExitCodeHandler
    {
        /// <summary>
        /// Обработка кодов возврата MSI
        /// </summary>
        /// <returns>true если установка успешна или требуется перезагрузка, false если ошибка</returns>
        public static bool HandleExitCode(int exitCode, string installerName, out string message)
        {
            switch (exitCode)
            {
                case 0:
                    message = "Успешно";
                    return true;

                case 3010:
                    message = "УСПЕШНО, ТРЕБУЕТСЯ ПЕРЕЗАГРУЗКА";
                    Logger.LogToFile($"⚠️ {installerName}: код {exitCode} - требуется перезагрузка", true);
                    return true; // Возвращаем true, но логируем как предупреждение

                case 1603:
                    message = "КРИТИЧЕСКАЯ ОШИБКА 1603: Не удалось установить\n" +
                              "Возможные причины:\n" +
                              "• Файл занят другим процессом\n" +
                              "• Недостаточно прав\n" +
                              "• Предыдущая версия не удалилась\n" +
                              "• Антивирус блокирует установку";
                    Logger.LogToFile($"❌ {installerName}: MSI ошибка 1603 - фатальная ошибка установки", true);
                    return false;

                case 1618:
                    message = "ОШИБКА 1618: Другая MSI-установка уже выполняется\n" +
                              "Подождите 1-2 минуты и попробуйте снова";
                    Logger.LogToFile($"⚠️ {installerName}: MSI ошибка 1618 - другая установка уже выполняется", true);
                    return false;

                case 1638:
                    message = "ОШИБКА 1638: Другая версия уже установлена";
                    Logger.LogToFile($"⚠️ {installerName}: MSI ошибка 1638 - другая версия уже установлена", true);
                    return false;

                default:
                    message = $"Неизвестная ошибка (код: {exitCode})";
                    Logger.LogToFile($"❌ {installerName}: ошибка с кодом {exitCode}", true);
                    return false;
            }
        }

        /// <summary>
        /// Запуск MSI с полным логированием
        /// </summary>
        public static async System.Threading.Tasks.Task<int> RunMsiWithLogging(
            string msiPath,
            string arguments,
            string logPrefix)
        {
            System.IO.Directory.CreateDirectory(AppPaths.LogsFolder);
            string logPath = System.IO.Path.Combine(
                AppPaths.LogsFolder,
                $"{logPrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.log");

            string fullArgs = $"{arguments} /lv \"{logPath}\"";

            Logger.LogToFile($"📦 Запуск MSI: {msiPath}");
            Logger.LogToFile($"   Аргументы: {fullArgs}");
            Logger.LogToFile($"   MSI лог: {logPath}");

            int exitCode = await ProcessRunner.RunAsync(msiPath, fullArgs, true);

            Logger.LogToFile($"   Выходной код: {exitCode}");

            if (exitCode != 0 && exitCode != 3010)
            {
                Logger.LogToFile($"❌ MSI лог сохранён: {logPath}", true);
            }

            return exitCode;
        }
    }
}