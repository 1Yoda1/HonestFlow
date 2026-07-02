using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Models;

namespace HonestFlow.Application.PointStatus
{
    public sealed class PointStatusService
    {
        private readonly bool _remoteConfigLoaded;
        private readonly int _ipCount;
        private readonly IReadOnlyList<IPData> _clients;

        public PointStatusService(bool remoteConfigLoaded, int ipCount, IReadOnlyList<IPData> clients = null)
        {
            _remoteConfigLoaded = remoteConfigLoaded;
            _ipCount = ipCount;
            _clients = clients ?? Array.Empty<IPData>();
        }

        public PointStatusResult Check()
        {
            var services = GetAllServiceSnapshots();

            return new PointStatusResult
            {
                Lm = CheckLmStatus(services),
                Controller = CheckExactServices(services, "esm-lm-controller"),
                Esm = CheckEsmServices(services),
                Kkt = CheckExactServices(services, "uem-agent", "uem-updater", "atol-grpc-service"),
                Cloud = CheckCloudStatus()
            };
        }

        private NodeStatus CheckLmStatus(ServiceSnapshot[] services)
        {
            var serviceStatus = CheckExactServices(services, "regime");
            var regimeService = serviceStatus.Services.FirstOrDefault();
            string serviceText = regimeService == null
                ? "служба не найдена"
                : regimeService.IsRunning ? "служба запущена" : $"служба: {regimeService.State}";

            try
            {
                using var api = new LmApiClient(enableDetailedLogging: false);
                var response = api.GetStatus().GetAwaiter().GetResult();

                if (!response.IsSuccess || response.Data == null)
                    return BuildLmApiUnavailableStatus(serviceStatus, serviceText, response.ErrorMessage);

                var status = response.Data;
                bool hasLmInn = !string.IsNullOrWhiteSpace(status.Inn);
                var client = hasLmInn ? FindClientByInn(status.Inn) : null;
                bool clientFound = client != null;

                var level = hasLmInn && !clientFound
                    ? NodeLevel.Warning
                    : serviceStatus.Level == NodeLevel.Error ? NodeLevel.Warning : serviceStatus.Level;

                string apiStatus = string.IsNullOrWhiteSpace(status.Status) ? "ответ есть" : status.Status;
                string clientName = clientFound ? client.Name : hasLmInn ? "не найден в списке" : "ИНН не указан";
                string innText = hasLmInn ? MaskInn(status.Inn) : "не указан";
                string matchText = clientFound ? "клиент найден по ИНН" : hasLmInn ? "клиент не найден по ИНН" : "ИНН ЛМ не указан";

                string statusText = $"API: {apiStatus}\nКлиент: {clientName}";
                string shortText = hasLmInn && !clientFound ? "Клиент?" : "API OK";
                string details =
                    $"Статус ЛМ ЧЗ: {apiStatus}\n" +
                    $"Служба Regime: {serviceText}\n" +
                    $"API: отвечает\n" +
                    $"Версия: {ValueOrDash(status.Version)}\n" +
                    $"Клиент: {clientName}\n" +
                    $"ИНН ЛМ: {innText}\n" +
                    $"Сопоставление: {matchText}";

                return new NodeStatus(level, shortText, details, serviceStatus.Services, statusText);
            }
            catch (Exception ex)
            {
                return BuildLmApiUnavailableStatus(serviceStatus, serviceText, ex.Message);
            }
        }

        private NodeStatus BuildLmApiUnavailableStatus(NodeStatus serviceStatus, string serviceText, string error)
        {
            var level = serviceStatus.Level == NodeLevel.Error ? NodeLevel.Error : NodeLevel.Warning;
            string statusText = "API: не отвечает\nКлиент: -";
            string details =
                "Статус ЛМ ЧЗ: API не отвечает\n" +
                $"Служба Regime: {serviceText}\n" +
                $"Ошибка API: {ValueOrDash(error)}";

            return new NodeStatus(level, "API нет", details, serviceStatus.Services, statusText);
        }

        private static ServiceSnapshot[] GetAllServiceSnapshots()
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
            startInfo.ArgumentList.Add("Get-Service | ForEach-Object { [Console]::WriteLine(('{0}|{1}' -f $_.Name, $_.Status)) }");

            using var process = Process.Start(startInfo);
            if (process == null)
                throw new InvalidOperationException("Не удалось запустить PowerShell для проверки служб.");

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(10000))
            {
                process.Kill();
                throw new TimeoutException("Проверка служб заняла слишком много времени.");
            }

            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                    ? "PowerShell не смог получить список служб."
                    : error.Trim());

            var services = ParsePowerShellServices(output);
            if (services.Length == 0)
                throw new InvalidOperationException("PowerShell вернул пустой список служб.");

            return services;
        }

        private static ServiceSnapshot[] ParsePowerShellServices(string output)
        {
            var services = new List<ServiceSnapshot>();

            foreach (string rawLine in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] parts = line.Split(new[] { '|' }, 2);
                if (parts.Length != 2)
                    continue;

                services.Add(new ServiceSnapshot(parts[0].Trim(), parts[1].Trim()));
            }

            return services.ToArray();
        }

        private static NodeStatus CheckEsmServices(ServiceSnapshot[] services)
        {
            var orchestrator = FindService(services, "esm-orchestrator");
            var cm = services.FirstOrDefault(x =>
                x.ServiceName.StartsWith("esm-cm-", StringComparison.OrdinalIgnoreCase));

            return BuildNodeStatus(new[]
            {
                ("esm-orchestrator", orchestrator),
                ("esm-cm-*", cm)
            });
        }

        private static NodeStatus CheckExactServices(ServiceSnapshot[] services, params string[] serviceNames)
        {
            return BuildNodeStatus(serviceNames
                .Select(name => (Name: name, Service: FindService(services, name)))
                .ToArray());
        }

        private static NodeStatus BuildNodeStatus((string Name, ServiceSnapshot Service)[] checks)
        {
            var missing = checks
                .Where(x => x.Service == null)
                .Select(x => x.Name)
                .ToArray();
            var present = checks
                .Where(x => x.Service != null)
                .Select(x => x.Service)
                .ToArray();

            if (missing.Length == checks.Length)
                return new NodeStatus(NodeLevel.Error, "Нет службы", $"Не найдены: {string.Join(", ", missing)}");

            if (missing.Length > 0)
            {
                var found = checks
                    .Where(x => x.Service != null)
                    .Select(x => $"{x.Name}: {x.Service.State}")
                    .ToArray();

                return new NodeStatus(
                    NodeLevel.Warning,
                    "Частично",
                    $"Найдены: {string.Join("; ", found)}\nНе найдены: {string.Join(", ", missing)}",
                    present);
            }

            var stopped = checks
                .Where(x => !x.Service.IsRunning)
                .Select(x => $"{x.Name}: {x.Service.State}")
                .ToArray();
            if (stopped.Length > 0)
                return new NodeStatus(NodeLevel.Warning, "Не запущено", string.Join("; ", stopped), present);

            return new NodeStatus(NodeLevel.Ok, "OK", "Все службы запущены", present);
        }

        private static ServiceSnapshot FindService(ServiceSnapshot[] services, string serviceName)
        {
            return services.FirstOrDefault(x =>
                string.Equals(x.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
        }

        private static string ValueOrDash(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        private IPData FindClientByInn(string inn)
        {
            string normalizedInn = NormalizeInn(inn);
            if (string.IsNullOrWhiteSpace(normalizedInn))
                return null;

            return _clients.FirstOrDefault(x =>
                string.Equals(NormalizeInn(x?.Inn), normalizedInn, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeInn(string inn)
        {
            if (string.IsNullOrWhiteSpace(inn))
                return string.Empty;

            return new string(inn.Where(char.IsDigit).ToArray());
        }

        private static string MaskInn(string inn)
        {
            if (string.IsNullOrWhiteSpace(inn) || inn.Length < 6)
                return inn ?? string.Empty;

            return inn.Substring(0, 4) + new string('*', Math.Max(0, inn.Length - 6)) + inn.Substring(inn.Length - 2);
        }

        private NodeStatus CheckCloudStatus()
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                using var response = client.GetAsync("https://cloud-api.yandex.net/v1/disk/").GetAwaiter().GetResult();
                bool internetOk = response.IsSuccessStatusCode ||
                                  (int)response.StatusCode == 401 ||
                                  (int)response.StatusCode == 404;

                if (_remoteConfigLoaded && _ipCount > 0)
                    return new NodeStatus(
                        internetOk ? NodeLevel.Ok : NodeLevel.Warning,
                        $"{_ipCount} ИП",
                        internetOk
                            ? $"Списки ИП загружены с Яндекс Диска: {_ipCount}"
                            : $"Списки ИП загружены, но проверка интернета не прошла: {_ipCount}");

                return new NodeStatus(
                    internetOk ? NodeLevel.Warning : NodeLevel.Error,
                    "Локально",
                    internetOk
                        ? "Интернет есть, но списки ИП взяты из локального файла"
                        : "Нет связи с облаком, используются локальные данные");
            }
            catch (Exception ex)
            {
                return new NodeStatus(
                    _remoteConfigLoaded && _ipCount > 0 ? NodeLevel.Warning : NodeLevel.Error,
                    _remoteConfigLoaded ? $"{_ipCount} ИП" : "Нет связи",
                    $"Проверка облака не удалась: {ex.Message}");
            }
        }
    }
}
