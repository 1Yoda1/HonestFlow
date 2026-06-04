using ESM_Installer_SPI.Classes;
using HonestFlow.Infrastructure;
using HonestFlow.Models;
using HonestFlow.Services.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HonestFlow.Services.System
{
    public class SystemService : ISystemService
    {
        private readonly ILogService _log;

        public SystemService(ILogService logService)
        {
            _log = logService;
        }

        /// <summary>
        /// Ожидание изменения состояния службы
        /// </summary>
        private async Task<bool> WaitForServiceStatus(string expectedStatus, int timeoutSeconds = 30)
        {
            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);

            while (DateTime.Now - startTime < timeout)
            {
                var status = await GetServiceStatus();
                if (status == expectedStatus)
                    return true;
                await Task.Delay(1000);
            }

            return false;
        }

        public async Task<bool> ManageService(string action)
        {
            try
            {
                switch (action.ToLower())
                {
                    case "stop":
                        await Task.Run(() => ProcessRunner.Run("net", "stop Regime", true));
                        return await WaitForServiceStatus("stopped", 30);

                    case "start":
                        await Task.Run(() => ProcessRunner.Run("net", "start Regime", true));
                        return await WaitForServiceStatus("running", 30);

                    case "restart":
                        await Task.Run(() => ProcessRunner.Run("net", "stop Regime", true));
                        if (!await WaitForServiceStatus("stopped", 30))
                            return false;

                        await Task.Run(() => ProcessRunner.Run("net", "start Regime", true));
                        return await WaitForServiceStatus("running", 30);

                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug($"ManageService ошибка при {action}: {ex.Message}");
                return false;
            }
        }

        public async Task<string> GetServiceStatus()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sc",
                    Arguments = "query Regime",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using var process = Process.Start(psi);
                string output = await process.StandardOutput.ReadToEndAsync();
                if (output.Contains("RUNNING")) return "running";
                if (output.Contains("STOPPED")) return "stopped";
                return "unknown";
            }
            catch
            {
                return "notfound";
            }
        }

        public async Task<bool> IsApiAvailable()
        {
            var lm = new LmModule("", "2.5.1-2");
            return await lm.IsApiAvailable();
        }

        public async Task<LmStatus> GetApiStatus()
        {
            var lm = new LmModule("", "2.5.1-2");
            return await lm.GetStatus();
        }

        public string GetSystemInfo()
        {
            var sb = new StringBuilder();

            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("                    СИСТЕМНАЯ ИНФОРМАЦИЯ");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine();

            sb.AppendLine("【ОПЕРАЦИОННАЯ СИСТЕМА】");
            sb.AppendLine($"  {Environment.OSVersion}");
            sb.AppendLine($"  64-разрядная: {Environment.Is64BitOperatingSystem}");
            sb.AppendLine();

            sb.AppendLine("【ПОЛЬЗОВАТЕЛЬ】");
            sb.AppendLine($"  Имя: {Environment.UserName}");
            sb.AppendLine($"  Компьютер: {Environment.MachineName}");
            sb.AppendLine($"  Администратор: {Utils.IsAdministrator()}");
            sb.AppendLine();

            sb.AppendLine("【ДИСКИ】");
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                sb.AppendLine($"  {drive.Name} — {drive.TotalFreeSpace / 1024 / 1024 / 1024} ГБ свободно из {drive.TotalSize / 1024 / 1024 / 1024} ГБ");
            }
            sb.AppendLine();

            sb.AppendLine("【УСТАНОВЛЕННЫЕ КОМПОНЕНТЫ】");
            sb.AppendLine($"  Драйвер АТОЛ: {VersionChecker.GetAtolDriverInfo()}");
            sb.AppendLine($"  ЕСМ: {VersionChecker.GetEsmVersion()}");
            sb.AppendLine($"  Контроллер: {VersionChecker.GetControllerVersion()}");
            sb.AppendLine();

            sb.AppendLine("【ЛОКАЛЬНЫЙ МОДУЛЬ ЧЗ】");
            try
            {
                var lm = new LmModule("", "2.5.1-2");
                var status = Task.Run(async () => await lm.GetStatus()).GetAwaiter().GetResult();
                if (status != null)
                {
                    sb.AppendLine($"  Версия: {status.version}");
                    sb.AppendLine($"  Статус: {status.status}");
                    sb.AppendLine($"  ИНН: {status.inn ?? "не задан"}");
                }
                else
                {
                    sb.AppendLine("  ❌ Не установлен или не отвечает");
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug($"Ошибка получения статуса ЛМ: {ex.Message}");
                sb.AppendLine("  ❌ Ошибка при проверке");
            }
            sb.AppendLine();

            sb.AppendLine("【ПРИЛОЖЕНИЕ】");
            sb.AppendLine($"  Путь: {Application.StartupPath}");
            sb.AppendLine($"  Версия: {Application.ProductVersion}");
            sb.AppendLine();

            sb.AppendLine("【ОЖИДАЕМЫЕ ВЕРСИИ (versions.json)】");
            var versions = ConfigManager.LoadVersions();
            sb.AppendLine($"  ЛМ ЧЗ: {versions?.lm_module ?? "не задана"}");
            sb.AppendLine($"  Драйвер АТОЛ: {versions?.atol_driver ?? "не задана"}");
            sb.AppendLine($"  ЕСМ: {versions?.esm ?? "не задана"}");
            sb.AppendLine($"  Контроллер: {versions?.controller ?? "не задана"}");
            sb.AppendLine();

            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            return sb.ToString();
        }
    }
}