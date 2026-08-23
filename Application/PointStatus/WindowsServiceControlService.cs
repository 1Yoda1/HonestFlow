using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.PointStatus
{
    public enum WindowsServiceControlFailure
    {
        ServiceNotFound,
        AccessDenied,
        Timeout
    }

    public sealed class WindowsServiceControlException : Exception
    {
        public WindowsServiceControlException(
            WindowsServiceControlFailure failure,
            string message,
            Exception innerException = null)
            : base(message, innerException) => Failure = failure;

        public WindowsServiceControlFailure Failure { get; }
    }

    public interface IWindowsServiceController : IDisposable
    {
        string ServiceName { get; }
        ServiceControllerStatus Status { get; }
        void Refresh();
        void Start();
        void Stop();
        void Continue();
    }

    public interface IWindowsServiceControllerFactory
    {
        IWindowsServiceController Create(string serviceName);
    }

    public sealed class WindowsServiceControlService
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(200);

        private readonly ILicenseOperationGuard _licenseGuard;
        private readonly IWindowsServiceControllerFactory _controllerFactory;
        private readonly TimeSpan _timeout;
        private readonly TimeSpan _pollInterval;

        public WindowsServiceControlService(ILicenseOperationGuard licenseGuard)
            : this(licenseGuard, new WindowsServiceControllerFactory(), DefaultTimeout, DefaultPollInterval)
        {
        }

        public WindowsServiceControlService(
            ILicenseOperationGuard licenseGuard,
            IWindowsServiceControllerFactory controllerFactory,
            TimeSpan timeout,
            TimeSpan pollInterval)
        {
            _licenseGuard = licenseGuard ?? throw new ArgumentNullException(nameof(licenseGuard));
            _controllerFactory = controllerFactory ?? throw new ArgumentNullException(nameof(controllerFactory));
            _timeout = timeout > TimeSpan.Zero ? timeout : throw new ArgumentOutOfRangeException(nameof(timeout));
            _pollInterval = pollInterval > TimeSpan.Zero ? pollInterval : throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }

        public async Task StartStoppedServicesAsync(
            IReadOnlyList<ServiceSnapshot> services,
            LicenseOperation operation,
            CancellationToken cancellationToken = default)
        {
            using var audit = Logger.BeginOperation("Запуск остановленных служб", nameof(WindowsServiceControlService));
            _licenseGuard.Demand(operation);
            foreach (ServiceSnapshot service in services.Where(item => !item.IsRunning))
                await ExecuteAsync(service.ServiceName, StartServiceCoreAsync, cancellationToken).ConfigureAwait(false);
        }

        public async Task RestartServicesAsync(
            IReadOnlyList<ServiceSnapshot> services,
            LicenseOperation operation,
            CancellationToken cancellationToken = default)
        {
            using var audit = Logger.BeginOperation("Перезапуск служб", nameof(WindowsServiceControlService));
            _licenseGuard.Demand(operation);
            foreach (ServiceSnapshot service in services)
                await ExecuteAsync(service.ServiceName, RestartServiceCoreAsync, cancellationToken).ConfigureAwait(false);
        }

        public async Task StartServiceAsync(string serviceName, LicenseOperation operation, CancellationToken cancellationToken = default)
        {
            using var audit = Logger.BeginOperation("Запуск службы " + serviceName, nameof(WindowsServiceControlService));
            _licenseGuard.Demand(operation);
            await ExecuteAsync(serviceName, StartServiceCoreAsync, cancellationToken).ConfigureAwait(false);
        }

        public async Task StopServiceAsync(string serviceName, LicenseOperation operation, CancellationToken cancellationToken = default)
        {
            using var audit = Logger.BeginOperation("Остановка службы " + serviceName, nameof(WindowsServiceControlService));
            _licenseGuard.Demand(operation);
            await ExecuteAsync(serviceName, StopServiceCoreAsync, cancellationToken).ConfigureAwait(false);
        }

        public async Task RestartServiceAsync(string serviceName, LicenseOperation operation, CancellationToken cancellationToken = default)
        {
            using var audit = Logger.BeginOperation("Перезапуск службы " + serviceName, nameof(WindowsServiceControlService));
            _licenseGuard.Demand(operation);
            await ExecuteAsync(serviceName, RestartServiceCoreAsync, cancellationToken).ConfigureAwait(false);
        }

        public bool IsServiceRunning(string serviceName)
        {
            try
            {
                using IWindowsServiceController service = _controllerFactory.Create(serviceName);
                service.Refresh();
                return service.Status == ServiceControllerStatus.Running;
            }
            catch (InvalidOperationException) { return false; }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1060) { return false; }
        }

        private async Task ExecuteAsync(
            string serviceName,
            Func<IWindowsServiceController, CancellationToken, Task> operation,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                throw new ArgumentException("Имя службы не задано.", nameof(serviceName));

            try
            {
                using IWindowsServiceController service = _controllerFactory.Create(serviceName);
                await operation(service, cancellationToken).ConfigureAwait(false);
            }
            catch (WindowsServiceControlException) { throw; }
            catch (InvalidOperationException ex)
            {
                throw Failure(WindowsServiceControlFailure.ServiceNotFound,
                    $"Служба «{serviceName}» не установлена или недоступна.", ex);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1060)
            {
                throw Failure(WindowsServiceControlFailure.ServiceNotFound,
                    $"Служба «{serviceName}» не установлена.", ex);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                throw Failure(WindowsServiceControlFailure.AccessDenied,
                    $"Недостаточно прав для управления службой «{serviceName}». Запустите HonestFlow от имени администратора.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw Failure(WindowsServiceControlFailure.AccessDenied,
                    $"Недостаточно прав для управления службой «{serviceName}». Запустите HonestFlow от имени администратора.", ex);
            }
        }

        private async Task StartServiceCoreAsync(IWindowsServiceController service, CancellationToken cancellationToken)
        {
            service.Refresh();
            switch (service.Status)
            {
                case ServiceControllerStatus.Running:
                    return;
                case ServiceControllerStatus.StartPending:
                case ServiceControllerStatus.ContinuePending:
                    await WaitForStatusAsync(service, ServiceControllerStatus.Running, cancellationToken).ConfigureAwait(false);
                    return;
                case ServiceControllerStatus.StopPending:
                    await WaitForStatusAsync(service, ServiceControllerStatus.Stopped, cancellationToken).ConfigureAwait(false);
                    break;
                case ServiceControllerStatus.PausePending:
                    await WaitForStatusAsync(service, ServiceControllerStatus.Paused, cancellationToken).ConfigureAwait(false);
                    service.Continue();
                    await WaitForStatusAsync(service, ServiceControllerStatus.Running, cancellationToken).ConfigureAwait(false);
                    return;
                case ServiceControllerStatus.Paused:
                    service.Continue();
                    await WaitForStatusAsync(service, ServiceControllerStatus.Running, cancellationToken).ConfigureAwait(false);
                    return;
            }

            service.Start();
            await WaitForStatusAsync(service, ServiceControllerStatus.Running, cancellationToken).ConfigureAwait(false);
        }

        private async Task StopServiceCoreAsync(IWindowsServiceController service, CancellationToken cancellationToken)
        {
            service.Refresh();
            switch (service.Status)
            {
                case ServiceControllerStatus.Stopped:
                    return;
                case ServiceControllerStatus.StopPending:
                    await WaitForStatusAsync(service, ServiceControllerStatus.Stopped, cancellationToken).ConfigureAwait(false);
                    return;
                case ServiceControllerStatus.StartPending:
                case ServiceControllerStatus.ContinuePending:
                    await WaitForStatusAsync(service, ServiceControllerStatus.Running, cancellationToken).ConfigureAwait(false);
                    break;
                case ServiceControllerStatus.PausePending:
                    await WaitForStatusAsync(service, ServiceControllerStatus.Paused, cancellationToken).ConfigureAwait(false);
                    break;
            }

            service.Stop();
            await WaitForStatusAsync(service, ServiceControllerStatus.Stopped, cancellationToken).ConfigureAwait(false);
        }

        private async Task RestartServiceCoreAsync(IWindowsServiceController service, CancellationToken cancellationToken)
        {
            await StopServiceCoreAsync(service, cancellationToken).ConfigureAwait(false);
            await StartServiceCoreAsync(service, cancellationToken).ConfigureAwait(false);
        }

        private async Task WaitForStatusAsync(
            IWindowsServiceController service,
            ServiceControllerStatus desiredStatus,
            CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            try
            {
                while (true)
                {
                    service.Refresh();
                    if (service.Status == desiredStatus) return;
                    await Task.Delay(_pollInterval, timeout.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw Failure(WindowsServiceControlFailure.Timeout,
                    $"Служба «{service.ServiceName}» не перешла в состояние {desiredStatus} за {_timeout.TotalSeconds:0} секунд.");
            }
        }

        private static WindowsServiceControlException Failure(
            WindowsServiceControlFailure failure,
            string message,
            Exception innerException = null) => new(failure, message, innerException);

        private sealed class WindowsServiceControllerFactory : IWindowsServiceControllerFactory
        {
            public IWindowsServiceController Create(string serviceName) =>
                new WindowsServiceControllerAdapter(new ServiceController(serviceName));
        }

        private sealed class WindowsServiceControllerAdapter : IWindowsServiceController
        {
            private readonly ServiceController _service;
            public WindowsServiceControllerAdapter(ServiceController service) => _service = service;
            public string ServiceName => _service.ServiceName;
            public ServiceControllerStatus Status => _service.Status;
            public void Refresh() => _service.Refresh();
            public void Start() => _service.Start();
            public void Stop() => _service.Stop();
            public void Continue() => _service.Continue();
            public void Dispose() => _service.Dispose();
        }
    }
}
