using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Lm;
using HonestFlow.Application.Core;
using HonestFlow.Application.Installation;
using HonestFlow.Application.RemoteAccess;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Models;

namespace HonestFlow.Application.PointStatus
{
    public sealed class PointStatusService : IPointStatusService
    {
        private readonly bool _remoteConfigLoaded;
        private readonly int _ipCount;
        private readonly IRuDesktopStatusProvider _ruDesktopService;
        private readonly IEsmStatusClient _esmStatusClient;
        private readonly IWindowsServiceSnapshotProvider _serviceSnapshotProvider;
        private readonly ILmStatusClient _lmApiClient;
        private readonly IControllerServiceInfoProbe _controllerServiceInfoProbe;
        private readonly ICloudConnectivityProbe _cloudConnectivityProbe;
        private readonly IKktPnpProbe _kktPnpProbe;
        private readonly IKktDriverProbe _kktDriverProbe;
        private readonly IKktPortProbe _kktPortProbe;
        private readonly IEsmApiPortProbe _esmApiPortProbe;
        public PointStatusService(
            bool remoteConfigLoaded,
            int ipCount,
            IReadOnlyList<IPData> clients = null,
            IRuDesktopStatusProvider ruDesktopService = null,
            IEsmStatusClient esmStatusClient = null,
            IWindowsServiceSnapshotProvider serviceSnapshotProvider = null,
            ILmStatusClient lmStatusClient = null,
            ICloudConnectivityProbe cloudConnectivityProbe = null,
            IKktPnpProbe kktPnpProbe = null,
            IControllerServiceInfoProbe controllerServiceInfoProbe = null,
            IKktDriverProbe kktDriverProbe = null,
            IKktPortProbe kktPortProbe = null,
            IEsmApiPortProbe esmApiPortProbe = null)
        {
            _remoteConfigLoaded = remoteConfigLoaded;
            _ipCount = ipCount;
            _ruDesktopService = ruDesktopService;
            _esmStatusClient = esmStatusClient ?? new EsmRestStatusClient();
            _serviceSnapshotProvider = serviceSnapshotProvider ?? new WindowsServiceSnapshotProvider();
            _lmApiClient = lmStatusClient ?? new LmApiClient(enableDetailedLogging: false);
            _controllerServiceInfoProbe = controllerServiceInfoProbe ?? new ControllerServiceInfoProbe();
            _cloudConnectivityProbe = cloudConnectivityProbe ?? new CloudConnectivityProbe();
            _kktPnpProbe = kktPnpProbe ?? new WindowsKktPnpProbe();
            _kktDriverProbe = kktDriverProbe ?? new KktDriverProbe();
            _kktPortProbe = kktPortProbe ?? new KktPortProbe();
            _esmApiPortProbe = esmApiPortProbe ?? new EsmApiPortProbe();
        }

        public Task<PointStatusResult> CheckAsync(CancellationToken cancellationToken) =>
            CheckAsync(null, cancellationToken);

        public async Task<PointStatusResult> CheckAsync(IPData currentClient, CancellationToken cancellationToken)
        {
            var services = await _serviceSnapshotProvider
                .GetSnapshotsAsync(cancellationToken)
                .ConfigureAwait(false);
            var controllerService = CheckExactServices(services, "esm-lm-controller");
            var kktService = CheckExactServices(services, "uem-agent", "uem-updater", "atol-grpc-service");
            var esmService = CheckEsmServices(services);
            Task<EsmStatusResult> controllerTask = _esmStatusClient.GetStatusAsync(cancellationToken);
            Task<ControllerServiceInfoResult> controllerServiceInfoTask = _controllerServiceInfoProbe.CheckAsync(cancellationToken);
            Task<EsmCashRegisterResult> kktTask = _esmStatusClient.GetCashRegisterStatusAsync(cancellationToken);
            Task<EsmRegistrationResult> esmTask;
            if (!IsEsmOrchestratorRunning(esmService))
            {
                esmTask = Task.FromResult<EsmRegistrationResult>(null);
            }
            else
            {
                esmTask = _esmStatusClient.GetRegistrationStatusAsync(cancellationToken);
            }
            Task<LmDiagnosticProbeResult> lmTask = CheckLmStatusAsync(services, currentClient);
            Task<NodeStatus> cloudTask = CheckCloudStatusAsync(cancellationToken);
            Task<NodeStatus> ruDesktopTask = CheckRuDesktopStatusAsync();
            Task<KktPnpResult> kktPnpTask = DetectKktPnpAsync(cancellationToken);
            Task<KktDriverProbeResult> kktDriverTask = _kktDriverProbe.CheckAsync(currentClient?.Architecture, cancellationToken);
            Task<KktPortProbeResult> kktPortTask = _kktPortProbe.CheckAsync(cancellationToken);
            await Task.WhenAll(controllerTask, controllerServiceInfoTask, kktTask, esmTask, lmTask, cloudTask, ruDesktopTask, kktPnpTask, kktDriverTask, kktPortTask).ConfigureAwait(false);
            EsmRegistrationResult esmRegistration = await esmTask.ConfigureAwait(false);
            EsmApiPortProbeResult esmApiPort =
                esmRegistration?.Kind == EsmRegistrationResultKind.Registered && IsEsmCmServiceRunning(esmService)
                    ? await _esmApiPortProbe.CheckAsync(cancellationToken).ConfigureAwait(false)
                    : null;
            LmDiagnosticProbeResult lmProbe = await lmTask.ConfigureAwait(false);
            NodeStatus lmStatus = lmProbe.Status;
            lmStatus = ApplyLmSystemRequirements(lmStatus, LmSystemRequirements.Check());

            return new PointStatusResult
            {
                Lm = lmStatus,
                Controller = BuildControllerStatus(controllerService, await controllerServiceInfoTask.ConfigureAwait(false), null),
                ControllerServiceStatus = controllerService,
                ControllerServiceInfo = await controllerServiceInfoTask.ConfigureAwait(false),
                Esm = BuildEsmStatus(esmService, esmApiPort, esmRegistration, null),
                Kkt = BuildKktStatus(
                    await kktPnpTask.ConfigureAwait(false),
                    await kktDriverTask.ConfigureAwait(false),
                    kktService,
                    await kktPortTask.ConfigureAwait(false),
                    null),
                Cloud = await cloudTask.ConfigureAwait(false),
                RuDesktop = await ruDesktopTask.ConfigureAwait(false),
                EsmApiStatus = controllerTask.Result,
                EsmRegistration = esmRegistration,
                EsmServiceStatus = esmService,
                EsmApiPort = esmApiPort,
                CashRegister = kktTask.Result,
                KktServiceStatus = kktService,
                KktDriver = await kktDriverTask.ConfigureAwait(false),
                KktPort4041 = await kktPortTask.ConfigureAwait(false),
                AtolDriverVersion = (await kktDriverTask.ConfigureAwait(false))?.ToLegacyDisplay(),
                KktPnP = await kktPnpTask.ConfigureAwait(false),
                LmProbe = lmProbe,
                ServiceSnapshots = services
            };
        }

        private async Task<KktPnpResult> DetectKktPnpAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _kktPnpProbe.DetectAsync(cancellationToken).ConfigureAwait(false) ??
                    KktPnpResult.Unavailable("PnP probe returned no result.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return KktPnpResult.Unavailable("PnP probe failed: " + ex.Message);
            }
        }

        private static bool IsEsmOrchestratorRunning(NodeStatus serviceStatus) =>
            serviceStatus.Services.Any(x =>
                string.Equals(x.ServiceName, "esm-orchestrator", StringComparison.OrdinalIgnoreCase) &&
                x.IsRunning);

        private static bool IsEsmCmServiceRunning(NodeStatus serviceStatus) =>
            serviceStatus.Services.Any(x =>
                x.ServiceName.StartsWith("esm-cm-", StringComparison.OrdinalIgnoreCase) &&
                x.IsRunning);

        public static NodeStatus ApplyLmSystemRequirements(
            NodeStatus status,
            LmSystemRequirementsResult requirements)
        {
            if (status == null || requirements == null || !requirements.HasWarnings)
                return status;

            var messages = requirements.MinimumWarnings
                .Concat(requirements.Recommendations)
                .ToArray();
            string requirementDetails = "Требования к ПК:\n- " + string.Join("\n- ", messages);
            NodeLevel level = status.Level == NodeLevel.Error ? NodeLevel.Error : NodeLevel.Warning;
            string statusText = status.StatusText;
            if (!requirements.MeetsMinimum)
                statusText = string.IsNullOrWhiteSpace(statusText)
                    ? "ПК ниже требований ЛМ ЧЗ"
                    : statusText + "\nПК ниже требований ЛМ ЧЗ";

            return new NodeStatus(
                level,
                status.ShortText,
                string.IsNullOrWhiteSpace(status.Details)
                    ? requirementDetails
                    : status.Details + "\n\n" + requirementDetails,
                status.Services,
                statusText,
                status.ActionKind);
        }

        public static NodeStatus BuildEsmStatus(
            NodeStatus serviceStatus,
            EsmApiPortProbeResult apiPort,
            EsmRegistrationResult registration,
            ComponentVersionStatus version)
        {
            IReadOnlyList<ServiceSnapshot> services = serviceStatus?.Services ?? Array.Empty<ServiceSnapshot>();
            ServiceSnapshot orchestrator = services.FirstOrDefault(service =>
                string.Equals(service.ServiceName, "esm-orchestrator", StringComparison.OrdinalIgnoreCase));
            ServiceSnapshot cm = services.FirstOrDefault(service =>
                service.ServiceName.StartsWith("esm-cm-", StringComparison.OrdinalIgnoreCase));

            if (version?.State == ComponentVersionState.NotInstalled || services.Count == 0)
                return EsmNode(NodeLevel.Error, "ЕСМ не установлен", services, apiPort, registration, version);
            if (orchestrator == null)
                return EsmNode(NodeLevel.Error, "Служба не найдена", services, apiPort, registration, version);
            if (!orchestrator.IsRunning)
                return EsmNode(NodeLevel.Error, "Служба остановлена", services, apiPort, registration, version);
            if (registration == null || registration.Kind == EsmRegistrationResultKind.Unavailable)
                return EsmNode(NodeLevel.Error, "Статус ЕСМ недоступен", services, apiPort, registration, version);
            if (registration.Kind == EsmRegistrationResultKind.NotConfigured)
                return EsmNode(NodeLevel.Error, "ЕСМ не зарегистрирован", services, apiPort, registration, version);
            // esm-cm-* is created only after the first successful ESM registration.
            // Its absence before this point is therefore not a service failure.
            if (cm == null)
                return EsmNode(NodeLevel.Error, "Служба не найдена", services, apiPort, registration, version);
            if (!cm.IsRunning)
                return EsmNode(NodeLevel.Error, "Служба остановлена", services, apiPort, registration, version);
            if (apiPort?.IsAvailable != true)
                return EsmNode(NodeLevel.Error, "ЕСМ недоступен", services, apiPort, registration, version);
            if (version?.State == ComponentVersionState.UpdateRequired)
                return EsmNode(NodeLevel.Warning, "Обновите ЕСМ", services, apiPort, registration, version);

            return EsmNode(NodeLevel.Ok, "Доступно", services, apiPort, registration, version);
        }

        private static NodeStatus EsmNode(
            NodeLevel level,
            string statusText,
            IReadOnlyList<ServiceSnapshot> services,
            EsmApiPortProbeResult apiPort,
            EsmRegistrationResult registration,
            ComponentVersionStatus version) =>
            new(level, statusText, BuildEsmDetails(services, apiPort, registration, version), services, statusText);

        private static string BuildEsmDetails(
            IReadOnlyList<ServiceSnapshot> services,
            EsmApiPortProbeResult apiPort,
            EsmRegistrationResult registration,
            ComponentVersionStatus version)
        {
            ServiceSnapshot orchestrator = services.FirstOrDefault(service =>
                string.Equals(service.ServiceName, "esm-orchestrator", StringComparison.OrdinalIgnoreCase));
            ServiceSnapshot cm = services.FirstOrDefault(service =>
                service.ServiceName.StartsWith("esm-cm-", StringComparison.OrdinalIgnoreCase));
            return $"Installed={version?.State != ComponentVersionState.NotInstalled}; " +
                   $"InstalledVersion={version?.InstalledVersion ?? "-"}; " +
                   $"TargetVersion={version?.TargetVersion ?? "-"}; " +
                   $"VersionMatch={version?.State == ComponentVersionState.Current}; " +
                   $"Orchestrator={orchestrator?.State ?? "missing"}; " +
                   $"CmService={cm?.ServiceName ?? "missing"}; " +
                   $"CmServiceState={cm?.State ?? "missing"}; " +
                   $"ApiPort={apiPort?.Port?.ToString() ?? "-"}; " +
                   $"ApiPortAvailable={apiPort?.IsAvailable == true}; " +
                   $"ApiPortError={apiPort?.ErrorCategory ?? "-"}; " +
                   $"Registration={registration?.Kind.ToString() ?? "Unknown"}.";
        }

        public static NodeStatus BuildKktStatus(
            KktPnpResult pnp,
            KktDriverProbeResult driver,
            NodeStatus serviceStatus,
            KktPortProbeResult port,
            ComponentVersionStatus driverVersion)
        {
            IReadOnlyList<ServiceSnapshot> services = serviceStatus?.Services ?? Array.Empty<ServiceSnapshot>();
            if (pnp?.Kind == KktPnpResultKind.NotDetected)
                return KktNode(NodeLevel.Error, "ККТ физически\nне подключена", pnp, driver, services, port, driverVersion);
            if (pnp?.Kind != KktPnpResultKind.Detected)
                return KktNode(NodeLevel.Warning, "Не удалось определить\nподключение ККТ", pnp, driver, services, port, driverVersion);
            if (driver?.IsAvailable != true || !driver.DriverFound || !driver.HasKnownVersion ||
                !driver.IsAtLeast(ComponentVersionRequirements.MinimumSupportedAtolDriver))
                return KktNode(NodeLevel.Error, "Драйвер для ЕСМ\nне установлен", pnp, driver, services, port, driverVersion);

            string missingService = RequiredKktServices.FirstOrDefault(name => services.All(service =>
                !string.Equals(service.ServiceName, name, StringComparison.OrdinalIgnoreCase)));
            if (missingService != null)
                return KktNode(NodeLevel.Error, "Служба не найдена", pnp, driver, services, port, driverVersion);
            ServiceSnapshot stoppedService = services.FirstOrDefault(service =>
                RequiredKktServices.Contains(service.ServiceName, StringComparer.OrdinalIgnoreCase) && !service.IsRunning);
            if (stoppedService != null)
                return KktNode(NodeLevel.Error, "Служба остановлена", pnp, driver, services, port, driverVersion);
            if (port?.IsAvailable != true)
                return KktNode(NodeLevel.Error, "Порт 4041\nне найден", pnp, driver, services, port, driverVersion);
            if (!string.IsNullOrWhiteSpace(driverVersion?.TargetVersion) && driver.IsBelow(driverVersion.TargetVersion))
                return KktNode(NodeLevel.Warning, "Обновите драйвер\nККТ", pnp, driver, services, port, driverVersion);

            return KktNode(NodeLevel.Ok, "Доступно", pnp, driver, services, port, driverVersion);
        }

        private static readonly string[] RequiredKktServices = { "uem-agent", "uem-updater", "atol-grpc-service" };

        private static NodeStatus KktNode(
            NodeLevel level,
            string statusText,
            KktPnpResult pnp,
            KktDriverProbeResult driver,
            IReadOnlyList<ServiceSnapshot> services,
            KktPortProbeResult port,
            ComponentVersionStatus driverVersion) =>
            new(level, statusText, BuildKktDetails(pnp, driver, services, port, driverVersion), services, statusText);

        private static string BuildKktDetails(
            KktPnpResult pnp,
            KktDriverProbeResult driver,
            IReadOnlyList<ServiceSnapshot> services,
            KktPortProbeResult port,
            ComponentVersionStatus driverVersion)
        {
            string missing = string.Join(",", RequiredKktServices.Where(name => services.All(service =>
                !string.Equals(service.ServiceName, name, StringComparison.OrdinalIgnoreCase))));
            string stopped = string.Join(",", services.Where(service =>
                RequiredKktServices.Contains(service.ServiceName, StringComparer.OrdinalIgnoreCase) && !service.IsRunning)
                .Select(service => service.ServiceName));
            return $"PnP={pnp?.Kind.ToString() ?? "Unknown"}; " +
                   $"PnPDetails={pnp?.Details ?? "-"}; " +
                   $"DriverArchitecture={driver?.RequiredArchitecture ?? "-"}; " +
                   $"DriverFound={driver?.DriverFound == true}; " +
                   $"InstalledVersion={driver?.InstalledVersion ?? "-"}; " +
                   $"MinimumSupportedVersion={ComponentVersionRequirements.MinimumSupportedAtolDriver}; " +
                   $"TargetVersion={driverVersion?.TargetVersion ?? "-"}; " +
                   $"MissingServices={(string.IsNullOrWhiteSpace(missing) ? "-" : missing)}; " +
                   $"StoppedServices={(string.IsNullOrWhiteSpace(stopped) ? "-" : stopped)}; " +
                   $"Port4041={(port?.IsAvailable == true ? "Success" : "Failure")}; " +
                   $"Port4041Error={port?.ErrorCategory ?? "-"}.";
        }

        public static NodeStatus BuildControllerStatus(
            NodeStatus serviceStatus,
            ControllerServiceInfoResult serviceInfo,
            ComponentVersionStatus version)
        {
            ServiceSnapshot service = serviceStatus?.Services?.FirstOrDefault();
            IReadOnlyList<ServiceSnapshot> services = serviceStatus?.Services ?? Array.Empty<ServiceSnapshot>();

            if (version?.State == ComponentVersionState.NotInstalled)
                return ControllerNode(NodeLevel.Error, "Контроллер не установлен", service, version, serviceInfo, services);
            if (service is null)
                return ControllerNode(NodeLevel.Error, "Служба не найдена", service, version, serviceInfo, services);
            if (!service.IsRunning)
                return ControllerNode(NodeLevel.Error, "Служба остановлена", service, version, serviceInfo, services);
            if (serviceInfo?.IsAvailable != true)
                return ControllerNode(NodeLevel.Error, "Контроллер недоступен", service, version, serviceInfo, services);
            if (version is null || version.State == ComponentVersionState.Unknown)
                return ControllerNode(NodeLevel.Warning, "Версия не определена", service, version, serviceInfo, services);
            if (version.State is ComponentVersionState.UpdateRequired or ComponentVersionState.BelowMinimum)
                return ControllerNode(NodeLevel.Warning, "Установите последнюю\nверсию", service, version, serviceInfo, services);

            return ControllerNode(NodeLevel.Ok, "Доступно", service, version, serviceInfo, services);
        }

        private static NodeStatus ControllerNode(
            NodeLevel level,
            string statusText,
            ServiceSnapshot service,
            ComponentVersionStatus version,
            ControllerServiceInfoResult serviceInfo,
            IReadOnlyList<ServiceSnapshot> services) =>
            new(level, statusText, BuildControllerDetails(service, version, serviceInfo), services, statusText);

        private static string BuildControllerDetails(
            ServiceSnapshot service,
            ComponentVersionStatus version,
            ControllerServiceInfoResult serviceInfo) =>
            $"Installed={version?.State != ComponentVersionState.NotInstalled}; " +
            $"InstalledVersion={version?.InstalledVersion ?? "-"}; " +
            $"TargetVersion={version?.TargetVersion ?? "-"}; " +
            $"VersionMatch={version?.State == ComponentVersionState.Current}; " +
            $"ServiceExists={service is not null}; " +
            $"ServiceRunning={service?.IsRunning == true}; " +
            $"ServiceInfoAvailable={serviceInfo?.IsAvailable == true}; " +
            $"ServiceInfoHttpStatus={serviceInfo?.HttpStatusCode?.ToString() ?? "-"}; " +
            $"ServiceInfoErrorCategory={serviceInfo?.ErrorCategory ?? "-"}.";

        private async Task<NodeStatus> CheckRuDesktopStatusAsync()
        {
            if (_ruDesktopService == null)
                return new NodeStatus(NodeLevel.Warning, "Не проверено", "Сервис проверки RuDesktop не инициализирован");

            var status = await _ruDesktopService.GetStatus().ConfigureAwait(false);
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

        private async Task<LmDiagnosticProbeResult> CheckLmStatusAsync(ServiceSnapshot[] services, IPData currentClient)
        {
            bool lmReady = false;
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
                var response = await _lmApiClient.GetStatus().ConfigureAwait(false);

                if (!response.IsSuccess || response.Data == null)
                    return new LmDiagnosticProbeResult(
                        BuildLmApiUnavailableStatus(serviceStatus, serviceText, response.ErrorMessage),
                        false,
                        serviceStatus.Services.Count == 0 ? LmDiagnosticProbeState.NotInstalled : LmDiagnosticProbeState.ApiUnavailable,
                        error: response.ErrorMessage);

                var status = response.Data;
                lmReady = string.Equals(status.Status, "ready", StringComparison.OrdinalIgnoreCase);
                bool initializing = string.Equals(status.Status, "initialization", StringComparison.OrdinalIgnoreCase);
                bool notConfigured =
                    string.Equals(status.Status, "not-configured", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status.Status, "not_configured", StringComparison.OrdinalIgnoreCase);
                string actualInn = NormalizeInn(status.Inn);
                string expectedInn = NormalizeInn(ResolveExpectedClientInn(currentClient));
                bool hasLmInn = !string.IsNullOrWhiteSpace(actualInn);
                bool hasExpectedInn = !string.IsNullOrWhiteSpace(expectedInn);
                LmInnComparisonState innComparison = !hasExpectedInn
                    ? LmInnComparisonState.Unknown
                    : !hasLmInn
                        ? LmInnComparisonState.Missing
                        : string.Equals(actualInn, expectedInn, StringComparison.Ordinal)
                            ? LmInnComparisonState.Match
                            : LmInnComparisonState.Mismatch;
                string actualInnMasked = hasLmInn ? MaskInn(actualInn) : "-";
                string expectedInnMasked = hasExpectedInn ? MaskInn(expectedInn) : "-";
                string comparisonDetails = innComparison switch
                {
                    LmInnComparisonState.Match => "LM INN matches expected current-client INN",
                    LmInnComparisonState.Mismatch => "LM INN differs from expected current-client INN",
                    LmInnComparisonState.Missing => "LM API returned no INN",
                    _ => "Expected client INN unavailable"
                };

                string apiStatus = string.IsNullOrWhiteSpace(status.Status) ? "ответ есть" : status.Status;
                string clientName = string.IsNullOrWhiteSpace(currentClient?.Name) ? "текущий клиент не определён" : currentClient.Name;
                string innText = hasLmInn ? actualInnMasked : "не указан";
                string matchText = comparisonDetails;

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

                if (notConfigured)
                {
                    return new LmDiagnosticProbeResult(new NodeStatus(
                        NodeLevel.Warning,
                        "Не настроена",
                        details + "\n\nИтог: ЛМ ЧЗ требуется инициализация.",
                        statusText: "ЛМ ЧЗ не настроена\nНажмите «Исправить»",
                        actionKind: NodeActionKind.InitializeLm), lmReady, LmDiagnosticProbeState.NotConfigured, status.Status,
                        innComparison: innComparison, actualInnMasked: actualInnMasked,
                        expectedInnMasked: expectedInnMasked, innComparisonDetails: comparisonDetails);
                }

                if (initializing)
                {
                    return new LmDiagnosticProbeResult(new NodeStatus(
                        NodeLevel.Warning,
                        "Инициализация",
                        details + "\n\nИтог: допустимое переходное состояние initialization.",
                        statusText: "ЛМ ЧЗ инициализируется\nНажмите «Обновить»"), lmReady, LmDiagnosticProbeState.Initializing, status.Status,
                        innComparison: innComparison, actualInnMasked: actualInnMasked,
                        expectedInnMasked: expectedInnMasked, innComparisonDetails: comparisonDetails);
                }

                if (!lmReady)
                {
                    return new LmDiagnosticProbeResult(new NodeStatus(
                        NodeLevel.Error,
                        "Не готова",
                        details + "\n\nИтог: API отвечает, но статус не ready.",
                        statusText: $"ЛМ ЧЗ: {apiStatus}\nОжидается ready"), lmReady,
                        string.Equals(status.Status, "sync_error", StringComparison.OrdinalIgnoreCase)
                            ? LmDiagnosticProbeState.SyncError
                            : LmDiagnosticProbeState.Failure,
                        status.Status,
                        innComparison: innComparison, actualInnMasked: actualInnMasked,
                        expectedInnMasked: expectedInnMasked, innComparisonDetails: comparisonDetails);
                }

                var readyLevel = serviceStatus.Level == NodeLevel.Ok ? NodeLevel.Ok : NodeLevel.Warning;
                return new LmDiagnosticProbeResult(new NodeStatus(
                    readyLevel,
                    "Ready",
                    details,
                    serviceStatus.Services,
                    statusText), true, LmDiagnosticProbeState.Available, status.Status,
                    innComparison: innComparison, actualInnMasked: actualInnMasked,
                    expectedInnMasked: expectedInnMasked, innComparisonDetails: comparisonDetails);
            }
            catch (Exception ex)
            {
                return new LmDiagnosticProbeResult(
                    BuildLmApiUnavailableStatus(serviceStatus, serviceText, ex.Message),
                    false,
                    serviceStatus.Services.Count == 0 ? LmDiagnosticProbeState.NotInstalled : LmDiagnosticProbeState.ApiUnavailable,
                    error: ex.Message);
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

        private static string ResolveExpectedClientInn(IPData currentClient) => currentClient?.Inn;

        private static EsmStatusDto GetEsmApiData(EsmStatusDto status) =>
            status?.Software?.Data ?? status?.Data?.Software?.Data ?? status?.Data ?? status;

        private static string NormalizeInn(string inn)
        {
            if (string.IsNullOrWhiteSpace(inn))
                return string.Empty;

            return inn.Trim();
        }

        private static string MaskInn(string inn)
        {
            if (string.IsNullOrWhiteSpace(inn) || inn.Length < 6)
                return inn ?? string.Empty;

            return inn.Substring(0, 4) + new string('*', Math.Max(0, inn.Length - 6)) + inn.Substring(inn.Length - 2);
        }

        private async Task<NodeStatus> CheckCloudStatusAsync(CancellationToken cancellationToken)
        {
            try
            {
                bool internetOk = await _cloudConnectivityProbe
                    .IsAvailableAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (_remoteConfigLoaded && _ipCount > 0)
                    return new NodeStatus(
                        internetOk ? NodeLevel.Ok : NodeLevel.Warning,
                        internetOk ? "Доступно" : "Загружено",
                        internetOk
                            ? "Удалённая конфигурация загружена с Яндекс Диска"
                            : "Удалённая конфигурация загружена, но проверка интернета не прошла");

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
                    _remoteConfigLoaded ? "Загружено" : "Нет связи",
                    $"Проверка облака не удалась: {ex.Message}");
            }
        }
    }
}
