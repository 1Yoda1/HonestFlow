using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.RemoteAccess;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Models;

namespace HonestFlow.Application.PointStatus
{
    public sealed class PointStatusService
    {
        private readonly bool _remoteConfigLoaded;
        private readonly int _ipCount;
        private readonly IReadOnlyList<IPData> _clients;
        private readonly RuDesktopService _ruDesktopService;
        private readonly IEsmStatusClient _esmStatusClient;
        public PointStatusService(bool remoteConfigLoaded, int ipCount, IReadOnlyList<IPData> clients = null, RuDesktopService ruDesktopService = null, IEsmStatusClient esmStatusClient = null)
        {
            _remoteConfigLoaded = remoteConfigLoaded;
            _ipCount = ipCount;
            _clients = clients ?? Array.Empty<IPData>();
            _ruDesktopService = ruDesktopService;
            _esmStatusClient = esmStatusClient ?? new EsmRestStatusClient();
        }

        public async Task<PointStatusResult> CheckAsync(CancellationToken cancellationToken)
        {
            var services = await Task.Run(GetAllServiceSnapshots, cancellationToken).ConfigureAwait(false);
            var controllerService = CheckExactServices(services, "esm-lm-controller");
            var kktService = CheckExactServices(services, "uem-agent", "uem-updater", "atol-grpc-service");
            var esmService = CheckEsmServices(services);
            Task<EsmStatusResult> controllerTask = controllerService.Services.Any(x => x.IsRunning)
                ? _esmStatusClient.GetStatusAsync(cancellationToken)
                : Task.FromResult<EsmStatusResult>(null);
            Task<EsmCashRegisterResult> kktTask = AreAllKktServicesRunning(kktService)
                ? _esmStatusClient.GetCashRegisterStatusAsync(cancellationToken)
                : Task.FromResult<EsmCashRegisterResult>(null);
            Task<EsmRegistrationResult> esmTask = IsEsmOrchestratorRunning(esmService)
                ? _esmStatusClient.GetRegistrationStatusAsync(cancellationToken)
                : Task.FromResult<EsmRegistrationResult>(null);
            await Task.WhenAll(controllerTask, kktTask, esmTask).ConfigureAwait(false);
            NodeStatus lmStatus = CheckLmStatus(services, out bool lmReady);

            return new PointStatusResult
            {
                Lm = lmStatus,
                Controller = BuildControllerStatus(controllerService, controllerTask.Result, lmReady, DateTime.Now),
                Esm = BuildEsmStatus(esmService, esmTask.Result, kktTask.Result),
                Kkt = BuildKktStatus(kktService, kktTask.Result),
                Cloud = CheckCloudStatus(),
                RuDesktop = CheckRuDesktopStatus()
            };
        }

        private static bool AreAllKktServicesRunning(NodeStatus serviceStatus) =>
            serviceStatus.Services.Count == 3 && serviceStatus.Services.All(x => x.IsRunning);

        private static bool AreAllEsmServicesRunning(NodeStatus serviceStatus) =>
            serviceStatus.Services.Count == 2 && serviceStatus.Services.All(x => x.IsRunning);

        private static bool IsEsmOrchestratorRunning(NodeStatus serviceStatus) =>
            serviceStatus.Services.Any(x =>
                string.Equals(x.ServiceName, "esm-orchestrator", StringComparison.OrdinalIgnoreCase) &&
                x.IsRunning);

        public static NodeStatus BuildEsmStatus(
            NodeStatus serviceStatus,
            EsmRegistrationResult registration,
            EsmCashRegisterResult cashRegister)
        {
            if (serviceStatus.Services.Count == 0)
            {
                return new NodeStatus(
                    NodeLevel.Error,
                    "Не установлен",
                    "Службы esm-orchestrator и esm-cm-* не найдены.\n\nУстановите ЕСМ.",
                    statusText: "ЕСМ не установлен\nУстановите ЕСМ");
            }

            if (serviceStatus.Services.Any(x => !x.IsRunning))
            {
                return new NodeStatus(
                    NodeLevel.Error,
                    "Службы не запущены",
                    $"Проверка регистрации ЕСМ не выполнялась.\n{serviceStatus.Details}",
                    serviceStatus.Services,
                    "Службы ЕСМ не запущены");
            }

            if (registration == null || registration.Kind == EsmRegistrationResultKind.Unavailable)
            {
                return new NodeStatus(
                    NodeLevel.Warning,
                    "Нет статуса",
                    "Службы ЕСМ работают, но GET /api/v1/instances/info не ответил.",
                    statusText: "Состояние регистрации ЕСМ не получено");
            }

            if (registration.Kind == EsmRegistrationResultKind.NotConfigured)
            {
                bool cashRegisterConnected = cashRegister?.Kind == EsmCashRegisterResultKind.Connected;
                string details = cashRegisterConnected
                    ? "Службы ЕСМ работают.\n" +
                      "GET /api/v1/instances/info: экземпляр не зарегистрирован.\n" +
                      "CashRegister.Data: получены — связь с кассой есть.\n\n" +
                      "Откройте ЕСМ и нажмите «Зарегистрировать»."
                    : "Службы ЕСМ работают, но экземпляр ЕСМ не зарегистрирован.\n\n" +
                      "Обратите внимание на строку «ККТ».\n" +
                      "После восстановления связи с кассой откройте ЕСМ и проверьте регистрацию.";

                return new NodeStatus(
                    cashRegisterConnected ? NodeLevel.Error : NodeLevel.Warning,
                    "Не зарегистрирован",
                    details,
                    statusText: cashRegisterConnected
                        ? "ЕСМ не зарегистрирован\nОткройте ЕСМ и нажмите «Зарегистрировать»"
                        : "ЕСМ не зарегистрирован\nПроверьте строку «ККТ»");
            }

            if (!AreAllEsmServicesRunning(serviceStatus))
            {
                return new NodeStatus(
                    NodeLevel.Warning,
                    "Неполная установка",
                    "Экземпляр ЕСМ зарегистрирован, но найден не полный набор служб ЕСМ.",
                    statusText: "Проверьте установку ЕСМ");
            }

            return new NodeStatus(
                NodeLevel.Ok,
                "Работает",
                "Службы ЕСМ работают.\nGET /api/v1/instances/info: экземпляр зарегистрирован.",
                serviceStatus.Services,
                "ЕСМ зарегистрирован");
        }

        public static NodeStatus BuildKktStatus(NodeStatus serviceStatus, EsmCashRegisterResult apiResult)
        {
            if (serviceStatus.Services.Count < 3)
            {
                return new NodeStatus(
                    NodeLevel.Error,
                    "Не установлен",
                    "Не найден полный набор служб uem-agent, uem-updater и atol-grpc-service.\n\n" +
                    "Драйвер ККТ для работы с ЕСМ не установлен.",
                    statusText: "Драйвер ККТ для работы с ЕСМ не установлен");
            }

            if (serviceStatus.Services.Any(x => !x.IsRunning))
            {
                return new NodeStatus(
                    NodeLevel.Error,
                    "Службы не запущены",
                    $"Проверка связи с ККТ не выполнялась.\n{serviceStatus.Details}",
                    serviceStatus.Services,
                    "Службы ККТ не запущены");
            }

            if (apiResult == null || apiResult.Kind == EsmCashRegisterResultKind.Unavailable)
            {
                return new NodeStatus(
                    NodeLevel.Warning,
                    "Нет статуса",
                    "Службы ККТ работают, но GET /api/v1/dkktList не ответил.",
                    statusText: "Состояние связи с ККТ не получено");
            }

            if (apiResult.Kind == EsmCashRegisterResultKind.NotConfigured)
            {
                return new NodeStatus(
                    NodeLevel.Warning,
                    "ЕСМ не настроен",
                    "Не найден gui_settings.json с портом локального API ЕСМ.",
                    statusText: "ЕСМ не настроен");
            }

            if (apiResult.Kind == EsmCashRegisterResultKind.Disconnected)
            {
                return new NodeStatus(
                    NodeLevel.Error,
                    "Нет связи с ККТ",
                    "ККТ не обнаружена\n\n" +
                    "Что удалось проверить:\n" +
                    "  ✓ Службы ККТ запущены\n" +
                    "  ✓ Локальный API ЕСМ отвечает\n" +
                    "  ✕ Данные подключённой кассы не получены\n\n" +
                    "Что нужно проверить:\n" +
                    "  1. ККТ включена и физически подключена к компьютеру.\n" +
                    "  2. Кабель USB/COM и порт подключения исправны.\n" +
                    "  3. ККТ выбрана и доступна в товароучётной системе.\n" +
                    "  4. После проверки перезапустите товароучётную систему.\n\n" +
                    "Затем нажмите «Обновить статусы» в HonestFlow.",
                    statusText: "Нет связи с ККТ\nПроверьте подключение и товароучётную систему");
            }

            return new NodeStatus(
                NodeLevel.Ok,
                "Работает",
                "Службы ККТ работают. Локальный API ЕСМ вернул CashRegister.Data.",
                serviceStatus.Services,
                "ККТ подключена");
        }

        public static NodeStatus BuildControllerStatus(NodeStatus serviceStatus, EsmStatusResult apiResult, bool lmReady, DateTime now)
        {
            var service = serviceStatus.Services.FirstOrDefault();
            if (service == null || !service.IsRunning)
                return new NodeStatus(NodeLevel.Error, "Служба остановлена", BuildControllerDetails(service, "API не запрашивался", null, "Служба остановлена или не найдена"), serviceStatus.Services, "Служба остановлена");

            if (apiResult == null || apiResult.Kind == EsmStatusResultKind.Unavailable)
                return new NodeStatus(NodeLevel.Warning, "Нет статуса", BuildControllerDetails(service, "API недоступен, ошибка HTTP или таймаут 3 с", null, "Состояние контроллера не получено"), serviceStatus.Services, "Служба запущена, состояние контроллера не получено");

            if (apiResult.Kind == EsmStatusResultKind.NotConfigured)
                return new NodeStatus(NodeLevel.Warning, "Не настроен", BuildControllerDetails(service, "API вернул 204 или экземпляр отсутствует", null, "ЕСМ не настроен"), serviceStatus.Services, "ЕСМ не настроен");

            var controller = apiResult.Status?.LmController;
            if (controller?.Code == null)
                return new NodeStatus(NodeLevel.Warning, "Нет статуса", BuildControllerDetails(service, "API ответил", controller, "В ответе отсутствует lmController.code"), serviceStatus.Services, "Состояние контроллера не получено");

            if (controller.Code != 0 || !string.IsNullOrWhiteSpace(controller.Error))
            {
                string error = string.IsNullOrWhiteSpace(controller.Error)
                    ? $"Код ошибки: {controller.Code}"
                    : controller.Error.Trim();
                return new NodeStatus(NodeLevel.Error, "Ошибка", BuildControllerDetails(service, "API ответил", controller, $"Ошибка контроллера: {error}", now), serviceStatus.Services, error);
            }

            if (apiResult.Status?.LmInfo == null)
            {
                if (!lmReady)
                {
                    return new NodeStatus(
                        NodeLevel.Warning,
                        "Ожидание ЛМ",
                        BuildControllerDetails(service, "API ответил", controller, "LM.Data отсутствует, но ЛМ ЧЗ на :5995 не готова; перезапуск контроллера не предлагается", now, hasLmData: false),
                        statusText: "Нет данных ЛМ\nСначала восстановите ЛМ ЧЗ");
                }

                return new NodeStatus(
                    NodeLevel.Error,
                    "Нет данных ЛМ",
                    BuildControllerDetails(service, "API ответил", controller, "LM.Data отсутствует", now, hasLmData: false),
                    serviceStatus.Services,
                    "Контроллер не получил данные ЛМ");
            }

            if (!lmReady)
            {
                return new NodeStatus(
                    NodeLevel.Warning,
                    "ЛМ не готова",
                    BuildControllerDetails(service, "API ответил", controller, "LM.Data получены, но ЛМ ЧЗ на :5995 сейчас не готова", now, hasLmData: true),
                    statusText: "Данные контроллера есть\nЛМ ЧЗ не готова");
            }

            return new NodeStatus(
                NodeLevel.Ok,
                "Работает",
                BuildControllerDetails(service, "API ответил", controller, "LM.Data получены", now, hasLmData: true),
                serviceStatus.Services,
                "Контроллер работает");
        }

        private static string BuildControllerDetails(ServiceSnapshot service, string apiState, EsmComponentStatus controller, string decision, DateTime? now = null, bool? hasLmData = null)
        {
            string serviceState = service == null ? "не найдена" : service.State;
            string code = controller?.Code?.ToString() ?? "-";
            string error = string.IsNullOrWhiteSpace(controller?.Error) ? "пусто" : controller.Error.Trim();
            string lastConnection = string.IsNullOrWhiteSpace(controller?.LastConnection) ? "-" : controller.LastConnection;
            string age = "не вычислен";
            if (now.HasValue && TryParseEsmTime(controller?.LastConnection, out var connectedAt))
                age = $"{Math.Max(0, (now.Value - connectedAt).TotalSeconds):0} сек";

            return
                $"Служба esm-lm-controller: {serviceState}\n" +
                "Источник контроллера: GET /api/v1/status/{id} → lmController\n" +
                "Проверка данных ЛМ: GET /api/v1/instances/lm/{id}\n" +
                $"Получение API: {apiState}\n" +
                $"lmController.code: {code}\n" +
                $"lmController.error: {error}\n" +
                $"lmController.lastConnection: {lastConnection}\n" +
                $"Возраст последней связи: {age}\n" +
                $"LM.Data: {(hasLmData.HasValue ? hasLmData.Value ? "получены" : "отсутствуют" : "не проверялись")}\n\n" +
                $"Итог: {decision}";
        }

        private static bool TryParseEsmTime(string value, out DateTime result) =>
            DateTime.TryParseExact(value, "HH:mm:ss dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out result);

        private NodeStatus CheckRuDesktopStatus()
        {
            if (_ruDesktopService == null)
                return new NodeStatus(NodeLevel.Warning, "Не проверено", "Сервис проверки RuDesktop не инициализирован");

            var status = _ruDesktopService.GetStatus().GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(status.ErrorMessage))
            {
                return new NodeStatus(
                    NodeLevel.Warning,
                    "Ошибка",
                    $"Ошибка проверки RuDesktop: {status.ErrorMessage}",
                    statusText: "Ошибка проверки");
            }

            if (status.InstallationState == RuDesktopInstallationState.NotInstalled)
            {
                return new NodeStatus(
                    NodeLevel.Error,
                    "Не найден",
                    "RuDesktop не найден на этом компьютере",
                    statusText: "Не установлен",
                    actionKind: NodeActionKind.InstallRuDesktop);
            }

            if (status.InstallationState == RuDesktopInstallationState.Damaged)
            {
                string details = status.IsInstalled
                    ? "Найден файл RuDesktop, но служба RuDesktop отсутствует"
                    : "Найдена служба RuDesktop, но исполняемый файл недоступен";
                return new NodeStatus(
                    NodeLevel.Error,
                    "Повреждён",
                    details,
                    statusText: "Требуется переустановка",
                    actionKind: NodeActionKind.ReinstallRuDesktop);
            }

            if (status.InstallationState == RuDesktopInstallationState.ServiceStopped)
            {
                return new NodeStatus(
                    NodeLevel.Warning,
                    "Служба остановлена",
                    "RuDesktop установлен, но его служба остановлена",
                    services: new[] { new ServiceSnapshot("RuDesktop", "Stopped") },
                    statusText: "Служба остановлена",
                    actionKind: NodeActionKind.ManageServices);
            }

            if (string.IsNullOrWhiteSpace(status.Id))
            {
                return new NodeStatus(
                    NodeLevel.Warning,
                    "ID не получен",
                    $"RuDesktop установлен\nСлужба: {FormatServiceStatus(status)}\nID: не удалось получить",
                    statusText: "ID не получен\nНажмите «Обновить»");
            }

            var level = status.ServiceInstalled && !status.ServiceRunning
                ? NodeLevel.Warning
                : NodeLevel.Ok;

            string passwordText = status.PasswordConfiguredByHonestFlow ? "пароль применён" : "пароль не применялся";
            return new NodeStatus(
                level,
                "Запросить помощь",
                $"RuDesktop установлен\nID: {status.Id}\nСлужба: {FormatServiceStatus(status)}\nПароль HonestFlow: {passwordText}",
                statusText: $"ID: {status.Id}\n{passwordText}",
                actionKind: NodeActionKind.RequestRuDesktopHelp);
        }

        private NodeStatus CheckLmStatus(ServiceSnapshot[] services, out bool lmReady)
        {
            lmReady = false;
            var serviceStatus = CheckExactServices(services, "regime", "yenisei");
            var regimeService = serviceStatus.Services.FirstOrDefault(x =>
                string.Equals(x.ServiceName, "regime", StringComparison.OrdinalIgnoreCase));
            var yeniseiService = serviceStatus.Services.FirstOrDefault(x =>
                string.Equals(x.ServiceName, "yenisei", StringComparison.OrdinalIgnoreCase));
            string serviceText = regimeService == null
                ? "служба не найдена"
                : regimeService.IsRunning ? "служба запущена" : $"служба: {regimeService.State}";
            string yeniseiText = yeniseiService == null
                ? "служба не найдена"
                : yeniseiService.IsRunning ? "служба запущена" : $"служба: {yeniseiService.State}";

            try
            {
                using var api = new LmApiClient(enableDetailedLogging: false);
                var response = api.GetStatus().GetAwaiter().GetResult();

                if (!response.IsSuccess || response.Data == null)
                    return BuildLmApiUnavailableStatus(serviceStatus, serviceText, response.ErrorMessage);

                var status = response.Data;
                lmReady = string.Equals(status.Status, "ready", StringComparison.OrdinalIgnoreCase);
                bool initializing = string.Equals(status.Status, "initialization", StringComparison.OrdinalIgnoreCase);
                bool notConfigured =
                    string.Equals(status.Status, "not-configured", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status.Status, "not_configured", StringComparison.OrdinalIgnoreCase);
                bool hasLmInn = !string.IsNullOrWhiteSpace(status.Inn);
                var client = hasLmInn ? FindClientByInn(status.Inn) : null;
                bool clientFound = client != null;

                string apiStatus = string.IsNullOrWhiteSpace(status.Status) ? "ответ есть" : status.Status;
                string clientName = clientFound ? client.Name : hasLmInn ? "не найден в списке" : "ИНН не указан";
                string innText = hasLmInn ? MaskInn(status.Inn) : "не указан";
                string matchText = clientFound ? "клиент найден по ИНН" : hasLmInn ? "клиент не найден по ИНН" : "ИНН ЛМ не указан";

                string statusText = $"API: {apiStatus}\nКлиент: {clientName}";
                string details =
                    $"Статус ЛМ ЧЗ: {apiStatus}\n" +
                    $"Служба Regime: {serviceText}\n" +
                    $"Служба Yenisei: {yeniseiText}\n" +
                    $"API: отвечает\n" +
                    $"Версия: {ValueOrDash(status.Version)}\n" +
                    $"Клиент: {clientName}\n" +
                    $"ИНН ЛМ: {innText}\n" +
                    $"Сопоставление: {matchText}";

                if (hasLmInn && !clientFound)
                {
                    return new NodeStatus(
                        NodeLevel.Error,
                        "Клиент не найден",
                        details + "\n\nИтог: клиент по ИНН не найден. Обратитесь к администратору.",
                        statusText: "Клиент по ИНН не найден\nОбратитесь к администратору");
                }

                if (notConfigured)
                {
                    return new NodeStatus(
                        NodeLevel.Warning,
                        "Не настроена",
                        details + "\n\nИтог: ЛМ ЧЗ требуется инициализация.",
                        statusText: "ЛМ ЧЗ не настроена\nНажмите «Исправить»",
                        actionKind: NodeActionKind.InitializeLm);
                }

                if (initializing)
                {
                    return new NodeStatus(
                        NodeLevel.Warning,
                        "Инициализация",
                        details + "\n\nИтог: допустимое переходное состояние initialization.",
                        statusText: "ЛМ ЧЗ инициализируется\nНажмите «Обновить»");
                }

                if (!lmReady)
                {
                    return new NodeStatus(
                        NodeLevel.Error,
                        "Не готова",
                        details + "\n\nИтог: API отвечает, но статус не ready.",
                        statusText: $"ЛМ ЧЗ: {apiStatus}\nОжидается ready");
                }

                var readyLevel = serviceStatus.Level == NodeLevel.Ok ? NodeLevel.Ok : NodeLevel.Warning;
                return new NodeStatus(
                    readyLevel,
                    "Ready",
                    details,
                    serviceStatus.Services,
                    statusText);
            }
            catch (Exception ex)
            {
                return BuildLmApiUnavailableStatus(serviceStatus, serviceText, ex.Message);
            }
        }

        private NodeStatus BuildLmApiUnavailableStatus(NodeStatus serviceStatus, string serviceText, string error)
        {
            var level = NodeLevel.Error;
            string statusText = "API: не отвечает\nКлиент: -";
            string details =
                "Статус ЛМ ЧЗ: API не отвечает\n" +
                $"Службы ЛМ: {serviceStatus.Details}\n" +
                $"Ошибка API: {ValueOrDash(error)}";
            bool needsServiceRecovery =
                serviceStatus.Services.Count > 0 &&
                (serviceStatus.Services.Count < 2 || serviceStatus.Services.Any(x => !x.IsRunning));

            return new NodeStatus(
                level,
                "API нет",
                details,
                needsServiceRecovery ? serviceStatus.Services : null,
                statusText,
                needsServiceRecovery ? NodeActionKind.RecoverLmServices : NodeActionKind.Default);
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

        private static string FormatServiceStatus(RuDesktopStatus status)
        {
            if (!status.ServiceInstalled)
                return "не найдена";

            return status.ServiceRunning ? "запущена" : "остановлена";
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
