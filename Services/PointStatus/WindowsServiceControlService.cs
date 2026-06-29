using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace HonestFlow.Services.PointStatus
{
    public sealed class WindowsServiceControlService
    {
        public void StartStoppedServices(IReadOnlyList<ServiceSnapshot> services)
        {
            foreach (var service in services.Where(x => !x.IsRunning))
                RunServicePowerShell("Start-Service", service.ServiceName);
        }

        public void RestartServices(IReadOnlyList<ServiceSnapshot> services)
        {
            foreach (var service in services)
                RunServicePowerShell("Restart-Service", service.ServiceName);
        }

        private static void RunServicePowerShell(string command, string serviceName)
        {
            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add($"{command} -Name '{serviceName.Replace("'", "''")}' -ErrorAction Stop");

            using var process = Process.Start(startInfo);
            if (process == null)
                throw new InvalidOperationException($"Не удалось запустить PowerShell для службы {serviceName}.");

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30000))
            {
                process.Kill();
                throw new TimeoutException($"Операция со службой {serviceName} заняла слишком много времени.");
            }

            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                    ? $"Команда {command} не выполнилась для {serviceName}. {output}".Trim()
                    : error.Trim());
        }
    }
}
