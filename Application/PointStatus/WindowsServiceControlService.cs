using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Models.Licensing;
using HonestFlow.Infrastructure;

namespace HonestFlow.Application.PointStatus
{
    public sealed class WindowsServiceControlService
    {
        private readonly ILicenseOperationGuard _licenseGuard;

        public WindowsServiceControlService(ILicenseOperationGuard licenseGuard)
        {
            _licenseGuard = licenseGuard ?? throw new ArgumentNullException(nameof(licenseGuard));
        }

        public async Task StartStoppedServicesAsync(
            IReadOnlyList<ServiceSnapshot> services,
            LicenseOperation operation)
        {
            using var audit = Logger.BeginOperation("Запуск остановленных служб", nameof(WindowsServiceControlService));
            _licenseGuard.Demand(operation);
            foreach (var service in services.Where(x => !x.IsRunning))
                await StartServiceCoreAsync(service.ServiceName).ConfigureAwait(false);
        }

        public async Task RestartServicesAsync(
            IReadOnlyList<ServiceSnapshot> services,
            LicenseOperation operation)
        {
            using var audit = Logger.BeginOperation("Перезапуск служб", nameof(WindowsServiceControlService));
            _licenseGuard.Demand(operation);
            foreach (var service in services)
                await RestartServiceCoreAsync(service.ServiceName).ConfigureAwait(false);
        }

        public Task StartServiceAsync(string serviceName, LicenseOperation operation)
        {
            using var audit = Logger.BeginOperation("Запуск службы " + serviceName, nameof(WindowsServiceControlService));
            _licenseGuard.Demand(operation);
            return StartServiceCoreAsync(serviceName);
        }

        public bool IsServiceRunning(string serviceName)
        {
            try
            {
                using var service = new ServiceController(serviceName);
                return service.Status == ServiceControllerStatus.Running;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static async Task StartServiceCoreAsync(string serviceName)
        {
            using var service = new ServiceController(serviceName);
            service.Refresh();
            if (service.Status == ServiceControllerStatus.Running)
                return;

            service.Start();
            await WaitForStatusAsync(service, ServiceControllerStatus.Running).ConfigureAwait(false);
        }

        private static async Task RestartServiceCoreAsync(string serviceName)
        {
            using var service = new ServiceController(serviceName);
            service.Refresh();
            if (service.Status != ServiceControllerStatus.Stopped)
            {
                service.Stop();
                await WaitForStatusAsync(service, ServiceControllerStatus.Stopped).ConfigureAwait(false);
            }

            service.Start();
            await WaitForStatusAsync(service, ServiceControllerStatus.Running).ConfigureAwait(false);
        }

        private static async Task WaitForStatusAsync(
            ServiceController service,
            ServiceControllerStatus desiredStatus)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (true)
            {
                service.Refresh();
                if (service.Status == desiredStatus)
                    return;

                try
                {
                    await Task.Delay(200, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw new System.TimeoutException(
                        $"Служба {service.ServiceName} не перешла в состояние {desiredStatus} за 30 секунд.");
                }
            }
        }
    }
}
