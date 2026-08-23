using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.PointStatus;
using HonestFlow.Models.Licensing;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class WindowsServiceControlServiceTests
    {
        [Fact]
        public async Task StartStoppedService_WaitsUntilRunning()
        {
            var controller = new FakeController(ServiceControllerStatus.Stopped);
            WindowsServiceControlService service = Create(controller);

            await service.StartServiceAsync("test-service", LicenseOperation.ManageServices);

            Assert.Equal(1, controller.StartCount);
            Assert.Equal(ServiceControllerStatus.Running, controller.Status);
            Assert.True(controller.RefreshCount >= 2);
        }

        [Fact]
        public async Task StartRunningService_IsIdempotent()
        {
            var controller = new FakeController(ServiceControllerStatus.Running);

            await Create(controller).StartServiceAsync("test-service", LicenseOperation.ManageServices);

            Assert.Equal(0, controller.StartCount);
            Assert.Equal(ServiceControllerStatus.Running, controller.Status);
        }

        [Fact]
        public async Task StartPendingService_WaitsInsteadOfStartingAgain()
        {
            var controller = new FakeController(ServiceControllerStatus.StartPending);
            controller.EnqueueRefresh(ServiceControllerStatus.Running);

            await Create(controller).StartServiceAsync("test-service", LicenseOperation.ManageServices);

            Assert.Equal(0, controller.StartCount);
            Assert.Equal(ServiceControllerStatus.Running, controller.Status);
        }

        [Fact]
        public async Task StopRunningService_WaitsUntilStopped()
        {
            var controller = new FakeController(ServiceControllerStatus.Running);

            await Create(controller).StopServiceAsync("test-service", LicenseOperation.ManageServices);

            Assert.Equal(1, controller.StopCount);
            Assert.Equal(ServiceControllerStatus.Stopped, controller.Status);
        }

        [Fact]
        public async Task StopStoppedService_IsIdempotent()
        {
            var controller = new FakeController(ServiceControllerStatus.Stopped);

            await Create(controller).StopServiceAsync("test-service", LicenseOperation.ManageServices);

            Assert.Equal(0, controller.StopCount);
        }

        [Fact]
        public async Task StopPendingService_WaitsInsteadOfStoppingAgain()
        {
            var controller = new FakeController(ServiceControllerStatus.StopPending);
            controller.EnqueueRefresh(ServiceControllerStatus.Stopped);

            await Create(controller).StopServiceAsync("test-service", LicenseOperation.ManageServices);

            Assert.Equal(0, controller.StopCount);
            Assert.Equal(ServiceControllerStatus.Stopped, controller.Status);
        }

        [Fact]
        public async Task Restart_WaitsForStoppedBeforeStarting()
        {
            var controller = new FakeController(ServiceControllerStatus.Running);

            await Create(controller).RestartServiceAsync("test-service", LicenseOperation.ManageServices);

            Assert.Equal(new[] { "Stop", "Stopped", "Start", "Running" }, controller.Events);
            Assert.Equal(ServiceControllerStatus.Running, controller.Status);
        }

        [Fact]
        public async Task InitialRefreshPreventsActionFromUsingStaleStatus()
        {
            var controller = new FakeController(ServiceControllerStatus.Stopped);
            controller.EnqueueRefresh(ServiceControllerStatus.Running);

            await Create(controller).StartServiceAsync("test-service", LicenseOperation.ManageServices);

            Assert.Equal(0, controller.StartCount);
            Assert.Equal(ServiceControllerStatus.Running, controller.Status);
        }

        [Fact]
        public async Task MissingService_ReturnsControlledFailure()
        {
            var factory = new ThrowingFactory(new InvalidOperationException("missing"));
            WindowsServiceControlService service = Create(factory, TimeSpan.FromMilliseconds(50));

            WindowsServiceControlException error = await Assert.ThrowsAsync<WindowsServiceControlException>(() =>
                service.StartServiceAsync("missing-service", LicenseOperation.ManageServices));

            Assert.Equal(WindowsServiceControlFailure.ServiceNotFound, error.Failure);
            Assert.Contains("не установлена", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task AccessDenied_ReturnsSpecificControlledFailure()
        {
            var controller = new FakeController(ServiceControllerStatus.Stopped)
            {
                StartException = new Win32Exception(5)
            };

            WindowsServiceControlException error = await Assert.ThrowsAsync<WindowsServiceControlException>(() =>
                Create(controller).StartServiceAsync("test-service", LicenseOperation.ManageServices));

            Assert.Equal(WindowsServiceControlFailure.AccessDenied, error.Failure);
            Assert.Contains("Недостаточно прав", error.Message);
        }

        [Fact]
        public async Task Timeout_ReturnsControlledFailureWithoutHanging()
        {
            var controller = new FakeController(ServiceControllerStatus.Stopped)
            {
                CompleteTransitions = false
            };
            WindowsServiceControlService service = Create(controller, TimeSpan.FromMilliseconds(20));

            WindowsServiceControlException error = await Assert.ThrowsAsync<WindowsServiceControlException>(() =>
                service.StartServiceAsync("test-service", LicenseOperation.ManageServices));

            Assert.Equal(WindowsServiceControlFailure.Timeout, error.Failure);
            Assert.Contains("не перешла", error.Message);
        }

        private static WindowsServiceControlService Create(
            FakeController controller,
            TimeSpan? timeout = null) =>
            Create(new FakeFactory(controller), timeout ?? TimeSpan.FromMilliseconds(100));

        private static WindowsServiceControlService Create(
            IWindowsServiceControllerFactory factory,
            TimeSpan timeout) =>
            new(new AllowLicenseGuard(), factory, timeout, TimeSpan.FromMilliseconds(1));

        private sealed class AllowLicenseGuard : ILicenseOperationGuard
        {
            public void Demand(LicenseOperation operation) { }
        }

        private sealed class FakeFactory : IWindowsServiceControllerFactory
        {
            private readonly FakeController _controller;
            public FakeFactory(FakeController controller) => _controller = controller;
            public IWindowsServiceController Create(string serviceName) => _controller;
        }

        private sealed class ThrowingFactory : IWindowsServiceControllerFactory
        {
            private readonly Exception _exception;
            public ThrowingFactory(Exception exception) => _exception = exception;
            public IWindowsServiceController Create(string serviceName) => throw _exception;
        }

        private sealed class FakeController : IWindowsServiceController
        {
            private readonly Queue<ServiceControllerStatus> _refreshStates = new();

            public FakeController(ServiceControllerStatus status) => Status = status;

            public string ServiceName => "test-service";
            public ServiceControllerStatus Status { get; private set; }
            public int RefreshCount { get; private set; }
            public int StartCount { get; private set; }
            public int StopCount { get; private set; }
            public bool CompleteTransitions { get; init; } = true;
            public Exception StartException { get; init; }
            public List<string> Events { get; } = new();

            public void EnqueueRefresh(ServiceControllerStatus status) => _refreshStates.Enqueue(status);

            public void Refresh()
            {
                RefreshCount++;
                if (_refreshStates.Count == 0) return;
                Status = _refreshStates.Dequeue();
                if (Status is ServiceControllerStatus.Running or ServiceControllerStatus.Stopped)
                    Events.Add(Status.ToString());
            }

            public void Start()
            {
                if (StartException != null) throw StartException;
                if (Status != ServiceControllerStatus.Stopped)
                    throw new InvalidOperationException("Start called before Stopped");
                StartCount++;
                Events.Add("Start");
                Status = ServiceControllerStatus.StartPending;
                if (CompleteTransitions) _refreshStates.Enqueue(ServiceControllerStatus.Running);
            }

            public void Stop()
            {
                if (Status is not (ServiceControllerStatus.Running or ServiceControllerStatus.Paused))
                    throw new InvalidOperationException("Stop called from invalid state");
                StopCount++;
                Events.Add("Stop");
                Status = ServiceControllerStatus.StopPending;
                if (CompleteTransitions) _refreshStates.Enqueue(ServiceControllerStatus.Stopped);
            }

            public void Continue()
            {
                Status = ServiceControllerStatus.ContinuePending;
                if (CompleteTransitions) _refreshStates.Enqueue(ServiceControllerStatus.Running);
            }

            public void Dispose() { }
        }
    }
}
