using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.PointStatus;
using HonestFlow.Application.RemoteAccess;
using HonestFlow.Models;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class PointStatusOrchestrationTests
    {
        [Fact]
        public async Task CheckAsync_StartsIndependentStatusChecksInParallel()
        {
            var gate = new ParallelGate(expectedEntrants: 6);
            var esmClient = new StubEsmClient(gate);
            var service = new PointStatusService(
                remoteConfigLoaded: true,
                ipCount: 1,
                serviceSnapshotProvider: new StubServices(),
                esmStatusClient: esmClient,
                lmStatusClient: new StubLmClient(gate),
                cloudConnectivityProbe: new StubCloudProbe(gate),
                ruDesktopService: new StubRuDesktopProvider(gate),
                kktPnpProbe: new StubKktPnpProbe(gate));

            Task<PointStatusResult> check = service.CheckAsync(CancellationToken.None);
            await gate.WaitUntilAllEnteredAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(check.IsCompleted);

            gate.Release();
            PointStatusResult result = await check.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.NotNull(result);
            Assert.Equal(6, gate.Entrants);
            Assert.Equal(0, esmClient.RegistrationRequests);
            Assert.Equal("Доступно", result.Cloud.ShortText);
            Assert.DoesNotContain("1", result.Cloud.ShortText);
            Assert.DoesNotContain("1", result.Cloud.Details);
        }

        [Fact]
        public async Task CheckAsync_PropagatesCancellationFromServiceSnapshotProvider()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var service = new PointStatusService(
                remoteConfigLoaded: false,
                ipCount: 0,
                serviceSnapshotProvider: new StubServices());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.CheckAsync(cancellation.Token));
        }

        private sealed class StubServices : IWindowsServiceSnapshotProvider
        {
            public Task<ServiceSnapshot[]> GetSnapshotsAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new[]
                {
                    Running("esm-lm-controller"),
                    Running("uem-agent"),
                    Running("uem-updater"),
                    Running("atol-grpc-service"),
                    Running("esm-orchestrator"),
                    Running("esm-cm-shop-42"),
                    Running("regime"),
                    Running("yenisei")
                });
            }

            private static ServiceSnapshot Running(string name) => new(name, "Running");
        }

        private sealed class StubEsmClient : IEsmStatusClient
        {
            private readonly ParallelGate _gate;
            public StubEsmClient(ParallelGate gate) => _gate = gate;
            public int RegistrationRequests { get; private set; }

            public async Task<EsmStatusResult> GetStatusAsync(CancellationToken cancellationToken)
            {
                await _gate.EnterAsync(cancellationToken);
                return EsmStatusResult.Success(new EsmStatusDto());
            }

            public async Task<EsmCashRegisterResult> GetCashRegisterStatusAsync(CancellationToken cancellationToken)
            {
                await _gate.EnterAsync(cancellationToken);
                return EsmCashRegisterResult.Connected();
            }

            public async Task<EsmRegistrationResult> GetRegistrationStatusAsync(CancellationToken cancellationToken)
            {
                RegistrationRequests++;
                await _gate.EnterAsync(cancellationToken);
                return EsmRegistrationResult.Registered();
            }
        }

        private sealed class StubLmClient : ILmStatusClient
        {
            private readonly ParallelGate _gate;
            public StubLmClient(ParallelGate gate) => _gate = gate;

            public async Task<ApiResponse<LmStatus>> GetStatus()
            {
                await _gate.EnterAsync(CancellationToken.None);
                return ApiResponse<LmStatus>.Success(
                    new LmStatus { Status = "ready", Version = "2.5.1" },
                    HttpStatusCode.OK,
                    "{}",
                    1);
            }
        }

        private sealed class StubCloudProbe : ICloudConnectivityProbe
        {
            private readonly ParallelGate _gate;
            public StubCloudProbe(ParallelGate gate) => _gate = gate;
            public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
            {
                await _gate.EnterAsync(cancellationToken);
                return true;
            }
        }

        private sealed class StubKktPnpProbe : IKktPnpProbe
        {
            private readonly ParallelGate _gate;
            public StubKktPnpProbe(ParallelGate gate) => _gate = gate;

            public async Task<KktPnpResult> DetectAsync(CancellationToken cancellationToken)
            {
                await _gate.EnterAsync(cancellationToken);
                return KktPnpResult.NotDetected();
            }
        }

        private sealed class StubRuDesktopProvider : IRuDesktopStatusProvider
        {
            private readonly ParallelGate _gate;
            public StubRuDesktopProvider(ParallelGate gate) => _gate = gate;
            public async Task<RuDesktopStatus> GetStatus()
            {
                await _gate.EnterAsync(CancellationToken.None);
                return new RuDesktopStatus
                {
                    IsInstalled = true,
                    ServiceInstalled = true,
                    ServiceRunning = true,
                    InstallationState = RuDesktopInstallationState.Ready,
                    Id = "123456"
                };
            }
        }

        private sealed class ParallelGate
        {
            private readonly int _expectedEntrants;
            private readonly TaskCompletionSource _allEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _entrants;

            public ParallelGate(int expectedEntrants) => _expectedEntrants = expectedEntrants;
            public int Entrants => Volatile.Read(ref _entrants);

            public async Task EnterAsync(CancellationToken cancellationToken)
            {
                if (Interlocked.Increment(ref _entrants) == _expectedEntrants)
                    _allEntered.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }

            public Task WaitUntilAllEnteredAsync() => _allEntered.Task;
            public void Release() => _release.TrySetResult();
        }
    }
}
