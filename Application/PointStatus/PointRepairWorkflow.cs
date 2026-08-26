using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.Lm;
using HonestFlow.Infrastructure;
using HonestFlow.Models;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.PointStatus
{
    public sealed class PointRepairWorkflow
    {
        private readonly WindowsServiceControlService _serviceControlService;
        private readonly LicensedLmInitializationService _lmInitializationService;
        private readonly Func<bool> _isAdministrator;

        public PointRepairWorkflow(
            WindowsServiceControlService serviceControlService,
            LicensedLmInitializationService lmInitializationService)
            : this(serviceControlService, lmInitializationService, Utils.IsAdministrator)
        {
        }

        public PointRepairWorkflow(
            WindowsServiceControlService serviceControlService,
            LicensedLmInitializationService lmInitializationService,
            Func<bool> isAdministrator)
        {
            _serviceControlService = serviceControlService ?? throw new ArgumentNullException(nameof(serviceControlService));
            _lmInitializationService = lmInitializationService ?? throw new ArgumentNullException(nameof(lmInitializationService));
            _isAdministrator = isAdministrator ?? throw new ArgumentNullException(nameof(isAdministrator));
        }

        public ServiceActionPlan CreateServiceActionPlan(NodeStatus status)
        {
            if (status == null || !status.CanManageServices)
                return null;

            bool shouldStart = status.Services.Any(service => !service.IsRunning);
            return new ServiceActionPlan(
                status,
                shouldStart,
                string.Join(", ", status.Services.Select(service => service.ServiceName)),
                _isAdministrator());
        }

        public Task ExecuteServiceActionAsync(
            ServiceActionPlan plan,
            LicenseOperation operation,
            CancellationToken cancellationToken = default)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            if (!plan.HasAdministratorAccess)
                throw new InvalidOperationException("Для управления службами нужны права администратора.");

            return plan.ShouldStart
                ? _serviceControlService.StartStoppedServicesAsync(plan.Status.Services, operation, cancellationToken)
                : _serviceControlService.RestartServicesAsync(plan.Status.Services, operation, cancellationToken);
        }

        public Task RestartServicesAsync(NodeStatus status, LicenseOperation operation, CancellationToken cancellationToken = default)
        {
            if (status?.Services == null || status.Services.Count == 0)
                throw new InvalidOperationException("Не найдены службы для перезапуска.");
            if (!_isAdministrator())
                throw new InvalidOperationException("Для управления службами нужны права администратора.");
            return _serviceControlService.RestartServicesAsync(status.Services, operation, cancellationToken);
        }

        public Task RestartServiceAsync(string serviceName, LicenseOperation operation, CancellationToken cancellationToken = default)
        {
            if (!_isAdministrator())
                throw new InvalidOperationException("Для управления службами нужны права администратора.");
            return _serviceControlService.RestartServiceAsync(serviceName, operation, cancellationToken);
        }

        public async Task RecoverLmServicesAsync(
            Action<string> progress,
            CancellationToken cancellationToken)
        {
            progress?.Invoke("Запускаем службу Regime...");
            await _serviceControlService.StartServiceAsync("regime", LicenseOperation.RecoverLmServices);

            progress?.Invoke("Ожидаем запуск Yenisei через Regime...");
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            if (!_serviceControlService.IsServiceRunning("yenisei"))
            {
                progress?.Invoke("Yenisei не запустилась автоматически. Запускаем...");
                await _serviceControlService.StartServiceAsync("yenisei", LicenseOperation.RecoverLmServices);
            }

            progress?.Invoke("Ожидаем готовность API ЛМ ЧЗ...");
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
        }

        public async Task<ApiSimpleResponse> InitializeLmAsync(
            string token,
            Action<string> progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Для инициализации ЛМ ЧЗ требуется токен.", nameof(token));

            progress?.Invoke("Инициализируем ЛМ ЧЗ...");
            ApiSimpleResponse result = await _lmInitializationService.InitializeAsync(token);
            if (!result.IsSuccess)
                return result;

            progress?.Invoke("Инициализация отправлена. Ожидаем изменение статуса...");
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }
    }

    public sealed class ServiceActionPlan
    {
        public ServiceActionPlan(
            NodeStatus status,
            bool shouldStart,
            string serviceList,
            bool hasAdministratorAccess)
        {
            Status = status;
            ShouldStart = shouldStart;
            ServiceList = serviceList;
            HasAdministratorAccess = hasAdministratorAccess;
        }

        public NodeStatus Status { get; }
        public bool ShouldStart { get; }
        public string ServiceList { get; }
        public bool HasAdministratorAccess { get; }
        public string ActionName => ShouldStart ? "запустить" : "перезапустить";
    }
}
