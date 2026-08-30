using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Core;
using HonestFlow.Application.Installation;
using HonestFlow.Application.PointStatus;
using HonestFlow.Infrastructure;
using HonestFlow.Models;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class KktBootstrapWorkflowTests
    {
        [Theory]
        [InlineData("x86")]
        [InlineData("x64")]
        public async Task UsesServerArchitectureAndEffectiveAtolVersion(string architecture)
        {
            var process = FakeProcess.Failure(KktBootstrapStartStatus.PortBusy);
            Fixture fixture = Create(process: process);

            await fixture.Workflow.RunAsync(Client(architecture, "10.10.9.17"), CancellationToken.None);

            Assert.Equal(architecture, process.Architecture);
            Assert.Equal("10.10.9.17", process.Version);
        }

        [Theory]
        [InlineData(KktBootstrapStartStatus.PortBusy, KktBootstrapStatus.KktPortBusy)]
        [InlineData(KktBootstrapStartStatus.DriverVersionMismatch, KktBootstrapStatus.DriverVersionMismatch)]
        public async Task StructuredHelperFailureStopsFlow(
            KktBootstrapStartStatus helperStatus,
            KktBootstrapStatus expected)
        {
            Fixture fixture = Create(process: FakeProcess.Failure(helperStatus));

            KktBootstrapResult result = await fixture.Workflow.RunAsync(Client(), CancellationToken.None);

            Assert.Equal(expected, result.Status);
            Assert.Equal(0, fixture.Esm.Calls);
            Assert.False(fixture.RegistrationClient.Called);
        }

        [Fact]
        public async Task FirstDiscoveryTimeoutRestartsEsmOnceThenRunsSecondPollAndCloses()
        {
            var session = new FakeSession();
            var clock = new FakeClock();
            Fixture fixture = Create(session: session, clock: clock, esm: FakeEsm.AlwaysDisconnected());

            KktBootstrapResult result = await fixture.Workflow.RunAsync(Client(), CancellationToken.None);

            Assert.Equal(KktBootstrapStatus.KktNotVisibleInEsm, result.Status);
            Assert.Equal(1, fixture.RestartCalls);
            Assert.True(fixture.Esm.Calls >= 4);
            Assert.Equal(1, session.CloseCalls);
        }

        [Fact]
        public async Task VisibleKktUsesExistingRegistrationAndSkipClosesHelper()
        {
            var session = new FakeSession();
            Fixture fixture = Create(session: session, esm: FakeEsm.Connected(), confirmWait: false);

            KktBootstrapResult result = await fixture.Workflow.RunAsync(Client(), CancellationToken.None);

            Assert.Equal(KktBootstrapStatus.RegistrationSuccessChannelSkipped, result.Status);
            Assert.True(fixture.RegistrationClient.Called);
            Assert.Equal(1, session.CloseCalls);
        }

        [Fact]
        public async Task RegistrationFailureClosesHelperAndDoesNotCheckGisMt()
        {
            var session = new FakeSession();
            int diagnosticsCalls = 0;
            Fixture fixture = Create(
                session: session,
                esm: FakeEsm.Connected(),
                registrationResult: TsPiotRegistrationResult.RegistrationFailed("synthetic"),
                diagnostics: () => { diagnosticsCalls++; return UnknownGis(); });

            KktBootstrapResult result = await fixture.Workflow.RunAsync(Client(), CancellationToken.None);

            Assert.Equal(KktBootstrapStatus.RegistrationFailed, result.Status);
            Assert.Equal(0, diagnosticsCalls);
            Assert.Equal(1, session.CloseCalls);
        }

        [Fact]
        public async Task GisMtReadyReturnsChannelReadyAndClosesHelper()
        {
            var session = new FakeSession();
            Fixture fixture = Create(
                session: session,
                esm: FakeEsm.Connected(),
                confirmWait: true,
                diagnostics: HealthyGis);

            KktBootstrapResult result = await fixture.Workflow.RunAsync(Client(), CancellationToken.None);

            Assert.Equal(KktBootstrapStatus.ChannelReady, result.Status);
            Assert.Equal(1, session.CloseCalls);
        }

        [Fact]
        public async Task GisMtTimeoutPreservesRegistrationSuccessAndLastSnapshot()
        {
            var session = new FakeSession();
            var clock = new FakeClock();
            Fixture fixture = Create(
                session: session,
                clock: clock,
                esm: FakeEsm.Connected(),
                confirmWait: true,
                diagnostics: UnknownGis);

            KktBootstrapResult result = await fixture.Workflow.RunAsync(Client(), CancellationToken.None);

            Assert.Equal(KktBootstrapStatus.RegistrationSuccessChannelTimeout, result.Status);
            Assert.True(result.RegistrationSucceeded);
            Assert.NotNull(result.LastDiagnostics);
            Assert.Equal(1, session.CloseCalls);
        }

        [Fact]
        public async Task CancellationAlwaysClosesHelper()
        {
            var session = new FakeSession();
            var clock = new FakeClock { CancelOnDelay = true };
            Fixture fixture = Create(session: session, clock: clock, esm: FakeEsm.AlwaysDisconnected());
            using var cancellation = new CancellationTokenSource();
            clock.Cancellation = cancellation;

            KktBootstrapResult result = await fixture.Workflow.RunAsync(Client(), cancellation.Token);

            Assert.Equal(KktBootstrapStatus.Cancelled, result.Status);
            Assert.Equal(1, session.CloseCalls);
        }

        [Fact]
        public async Task ExceptionAlwaysClosesHelper()
        {
            var session = new FakeSession();
            Fixture fixture = Create(session: session, esm: FakeEsm.Throwing());

            KktBootstrapResult result = await fixture.Workflow.RunAsync(Client(), CancellationToken.None);

            Assert.Equal(KktBootstrapStatus.HelperFailed, result.Status);
            Assert.Equal(1, session.CloseCalls);
        }

        [Fact]
        public void ProtocolParsesConnectedAndMasksSerial()
        {
            KktBootstrapProtocolEvent message = KktBootstrapProtocolEvent.Parse(
                "CONNECTED|arch=x86|driverVersion=10.10.8.23|model=ATOL 30F|serial=00106902170527|firmware=5.16");

            KktBootstrapConnectionInfo info = message.ToConnectionInfo();
            Assert.Equal("CONNECTED", message.Name);
            Assert.Equal("x86", info.Architecture);
            Assert.Equal("10.10.8.23", info.DriverVersion);
            Assert.Equal("ATOL 30F", info.Model);
            Assert.EndsWith("0527", info.MaskedSerial);
            Assert.DoesNotContain("00106902170527", info.MaskedSerial);
        }

        [Theory]
        [InlineData("ERROR|code=PORT_BUSY|driverCode=5|message=busy", KktBootstrapStartStatus.PortBusy)]
        [InlineData("ERROR|code=DRIVER_VERSION_MISMATCH|driverCode=0|message=wrong", KktBootstrapStartStatus.DriverVersionMismatch)]
        public void ProtocolMapsStructuredErrors(string line, KktBootstrapStartStatus expected)
        {
            Assert.Equal(expected, KktBootstrapProtocolEvent.Parse(line).ToStartFailure().Status);
        }

        [Theory]
        [InlineData("WAITING_FOR_CLOSE")]
        [InlineData("PONG")]
        [InlineData("CLOSED")]
        public void ProtocolParsesLifecycleEvents(string line)
        {
            Assert.Equal(line, KktBootstrapProtocolEvent.Parse(line).Name);
        }

        [Theory]
        [InlineData("x86", 0x014c)]
        [InlineData("x64", 0x8664)]
        public void EmbeddedHelperCanBeExtractedWithExpectedPeArchitecture(string architecture, int expectedMachine)
        {
            string directory = Path.Combine(Path.GetTempPath(), "honestflow-kkt-bootstrap-" + Guid.NewGuid().ToString("N"));
            try
            {
                string path = new KktBootstrapResourceExtractor(runtimeDirectory: directory).Extract(architecture);
                byte[] bytes = File.ReadAllBytes(path);
                int peOffset = BitConverter.ToInt32(bytes, 0x3c);
                int machine = BitConverter.ToUInt16(bytes, peOffset + 4);

                Assert.Equal(expectedMachine, machine);
                Assert.True(bytes.Length > 1_000_000);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }

        private static Fixture Create(
            FakeProcess process = null,
            FakeSession session = null,
            FakeClock clock = null,
            FakeEsm esm = null,
            bool confirmWait = false,
            TsPiotRegistrationResult registrationResult = null,
            Func<DiagnosticsSnapshot> diagnostics = null)
        {
            session ??= new FakeSession();
            process ??= FakeProcess.Connected(session);
            clock ??= new FakeClock();
            esm ??= FakeEsm.Connected();
            var registrationClient = new FakeRegistrationClient(registrationResult ?? TsPiotRegistrationResult.Success());
            var registration = new TsPiotRegistrationWorkflow(
                registrationClient,
                new FakePortProbe(),
                new FakeLog());
            var fixture = new Fixture(process, session, clock, esm, registrationClient);
            fixture.Workflow = new KktBootstrapWorkflow(
                process,
                esm,
                registration,
                _ => { fixture.RestartCalls++; return Task.CompletedTask; },
                _ => Task.FromResult((diagnostics ?? UnknownGis)()),
                _ => Task.FromResult(confirmWait),
                new FakeProgress(),
                new KktBootstrapWorkflowOptions
                {
                    KktDiscoveryTimeout = TimeSpan.FromSeconds(2),
                    KktPollInterval = TimeSpan.FromSeconds(1),
                    GisMtTimeout = TimeSpan.FromSeconds(4),
                    GisMtPollInterval = TimeSpan.FromSeconds(1)
                },
                () => clock.Now,
                clock.DelayAsync);
            return fixture;
        }

        private static IPData Client(string architecture = "x64", string version = "10.10.8.23") => new()
        {
            Architecture = architecture,
            Versions = new VersionsData { AtolDriver = version }
        };

        private static DiagnosticsSnapshot HealthyGis() => new()
        {
            Gismt = new DiagnosticComponentFact(DiagnosticState.Healthy, "Работает")
        };

        private static DiagnosticsSnapshot UnknownGis() => new()
        {
            Gismt = new DiagnosticComponentFact(DiagnosticState.Unknown, "Не удалось проверить")
        };

        private sealed class Fixture
        {
            public Fixture(FakeProcess process, FakeSession session, FakeClock clock, FakeEsm esm, FakeRegistrationClient registrationClient)
            {
                Process = process; Session = session; Clock = clock; Esm = esm; RegistrationClient = registrationClient;
            }
            public KktBootstrapWorkflow Workflow { get; set; }
            public FakeProcess Process { get; }
            public FakeSession Session { get; }
            public FakeClock Clock { get; }
            public FakeEsm Esm { get; }
            public FakeRegistrationClient RegistrationClient { get; }
            public int RestartCalls { get; set; }
        }

        private sealed class FakeProcess : IKktBootstrapProcessClient
        {
            private readonly KktBootstrapStartStatus _status;
            private readonly FakeSession _session;
            private FakeProcess(KktBootstrapStartStatus status, FakeSession session) { _status = status; _session = session; }
            public string Architecture { get; private set; }
            public string Version { get; private set; }
            public static FakeProcess Connected(FakeSession session) => new(KktBootstrapStartStatus.Connected, session);
            public static FakeProcess Failure(KktBootstrapStartStatus status) => new(status, null);
            public Task<KktBootstrapStartResult> StartAsync(string architecture, string requiredDriverVersion, CancellationToken cancellationToken)
            {
                Architecture = architecture; Version = requiredDriverVersion;
                return Task.FromResult(_status == KktBootstrapStartStatus.Connected
                    ? KktBootstrapStartResult.Connected(_session, new KktBootstrapConnectionInfo())
                    : KktBootstrapStartResult.Error(_status, _status.ToString().ToUpperInvariant(), "synthetic"));
            }
        }

        private sealed class FakeSession : IKktBootstrapSession
        {
            public bool IsRunning => CloseCalls == 0;
            public int CloseCalls { get; private set; }
            public Task CloseAsync(CancellationToken cancellationToken) { CloseCalls++; return Task.CompletedTask; }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private sealed class FakeClock
        {
            public DateTimeOffset Now { get; private set; } = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
            public bool CancelOnDelay { get; set; }
            public CancellationTokenSource Cancellation { get; set; }
            public Task DelayAsync(TimeSpan delay, CancellationToken token)
            {
                Now += delay;
                if (CancelOnDelay)
                {
                    Cancellation?.Cancel();
                    token.ThrowIfCancellationRequested();
                }
                return Task.CompletedTask;
            }
        }

        private sealed class FakeEsm : IEsmStatusClient
        {
            private readonly Func<EsmCashRegisterResult> _result;
            private FakeEsm(Func<EsmCashRegisterResult> result) => _result = result;
            public int Calls { get; private set; }
            public static FakeEsm Connected() => new(() => EsmCashRegisterResult.Connected());
            public static FakeEsm AlwaysDisconnected() => new(() => EsmCashRegisterResult.Disconnected());
            public static FakeEsm Throwing() => new(() => throw new InvalidOperationException("synthetic"));
            public Task<EsmCashRegisterResult> GetCashRegisterStatusAsync(CancellationToken cancellationToken)
            {
                Calls++; return Task.FromResult(_result());
            }
            public Task<EsmStatusResult> GetStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<EsmRegistrationResult> GetRegistrationStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        }

        private sealed class FakeRegistrationClient : IEsmTsPiotRegistrationClient
        {
            private readonly TsPiotRegistrationResult _result;
            public FakeRegistrationClient(TsPiotRegistrationResult result) => _result = result;
            public bool Called { get; private set; }
            public Task<TsPiotRegistrationResult> RegisterAsync(CancellationToken cancellationToken)
            {
                Called = true; return Task.FromResult(_result);
            }
        }

        private sealed class FakePortProbe : IEsmApiPortProbe
        {
            public Task<EsmApiPortProbeResult> CheckAsync(CancellationToken cancellationToken) =>
                Task.FromResult(EsmApiPortProbeResult.Available(51077));
        }

        private sealed class FakeProgress : IProgressService
        {
            public readonly List<string> Steps = new();
            public void SetProgress(int percent, string stepName) => Steps.Add(stepName);
        }

        private sealed class FakeLog : ILogService
        {
            public void LogUser(string message, bool isError = false) { }
            public void LogDebug(string message) { }
            public string GetUserLog() => string.Empty;
        }
    }
}
