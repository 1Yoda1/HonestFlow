using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Installation;
using HonestFlow.Application.PointStatus;
using HonestFlow.Infrastructure;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class KktTopologyStatusTests
    {
        [Theory]
        [InlineData("ATOL USB device", null, null)]
        [InlineData(null, "ККТ ATOL 30Ф", null)]
        [InlineData(null, null, "AtOl")]
        public void AtolPnpMatcher_DetectsPresentDeviceRegardlessOfPort(string name, string friendlyName, string manufacturer)
        {
            Assert.True(KktPnpDeviceMatcher.IsAtol(name, friendlyName, manufacturer));
        }

        [Fact]
        public void PnpNotDetected_IsRedPhysicalDisconnect()
        {
            NodeStatus status = Build(KktPnpResult.NotDetected());
            Assert.Equal(NodeLevel.Error, status.Level);
            Assert.Equal("ККТ физически\nне подключена", status.StatusText);
        }

        [Fact]
        public void PnpUnavailable_IsUnknownNotFalsePhysicalDisconnect()
        {
            NodeStatus status = Build(KktPnpResult.Unavailable("setupapi_failed"));
            Assert.Equal(NodeLevel.Warning, status.Level);
            Assert.DoesNotContain("физически", status.StatusText, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("x64")]
        [InlineData("x86")]
        public async Task RequiredArchitecture_DoesNotUseTheOtherDriverPath(string requiredArchitecture)
        {
            string existing = Path.GetTempFileName();
            string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe");
            try
            {
                var probe = requiredArchitecture == "x64"
                    ? new KktDriverProbe(existing, missing)
                    : new KktDriverProbe(missing, existing);

                KktDriverProbeResult result = await probe.CheckAsync(requiredArchitecture, CancellationToken.None);

                Assert.False(result.DriverFound);
                Assert.Equal(requiredArchitecture, result.RequiredArchitecture);
            }
            finally
            {
                File.Delete(existing);
            }
        }

        [Fact]
        public void DriverBelowMinimum_IsRed()
        {
            NodeStatus status = Build(driver: KktDriverProbeResult.Found("x64", "10.10.8.22"));
            Assert.Equal(NodeLevel.Error, status.Level);
            Assert.Equal("Драйвер для ЕСМ\nне установлен", status.StatusText);
        }

        [Fact]
        public void SupportedDriverBelowTarget_IsYellow()
        {
            NodeStatus status = Build(driverVersion: DriverVersion("10.10.8.24"));
            Assert.Equal(NodeLevel.Warning, status.Level);
            Assert.Equal("Обновите драйвер\nККТ", status.StatusText);
        }

        [Fact]
        public void RequiredDriverAtTarget_IsGreen()
        {
            NodeStatus status = Build(driver: KktDriverProbeResult.Found("x64", "10.10.8.24"), driverVersion: DriverVersion("10.10.8.24"));
            Assert.Equal(NodeLevel.Ok, status.Level);
            Assert.Equal("Доступно", status.StatusText);
        }

        [Fact]
        public void MissingOrStoppedService_IsRedAndNamesServiceInDeveloperDetails()
        {
            NodeStatus missing = Build(services: Services(("uem-agent", "Running"), ("atol-grpc-service", "Running")));
            NodeStatus stopped = Build(services: Services(("uem-agent", "Running"), ("uem-updater", "Stopped"), ("atol-grpc-service", "Running")));

            Assert.Equal("Служба не найдена", missing.StatusText);
            Assert.Contains("MissingServices=uem-updater", missing.Details);
            Assert.Equal("Служба остановлена", stopped.StatusText);
            Assert.Contains("StoppedServices=uem-updater", stopped.Details);
        }

        [Fact]
        public void PortUnavailable_IsRed()
        {
            NodeStatus status = Build(port: KktPortProbeResult.Unavailable("refused"));
            Assert.Equal(NodeLevel.Error, status.Level);
            Assert.Equal("Порт 4041\nне найден", status.StatusText);
            Assert.Contains("Port4041Error=refused", status.Details);
        }

        [Fact]
        public async Task PortProbe_DistinguishesListeningAndRefused()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            try
            {
                var probe = new KktPortProbe("127.0.0.1", port, TimeSpan.FromSeconds(1));
                Assert.True((await probe.CheckAsync(CancellationToken.None)).IsAvailable);
            }
            finally
            {
                listener.Stop();
            }

            var refusedProbe = new KktPortProbe("127.0.0.1", port, TimeSpan.FromSeconds(1));
            Assert.Contains((await refusedProbe.CheckAsync(CancellationToken.None)).ErrorCategory, new[] { "refused", "timeout" });
        }

        [Fact]
        public void KktNodeAndEsmLinkAreIndependent()
        {
            PointStatusResult result = Result(NodeLevel.Ok, EsmCashRegisterResult.Disconnected());
            DiagnosticsSnapshot snapshot = new DiagnosticsSnapshotBuilder().Create(result);

            Assert.Equal(DiagnosticState.Healthy, snapshot.Kkt.State);
            Assert.Equal(DiagnosticConnectionState.Disconnected, snapshot.EsmToKkt.State);
        }

        [Fact]
        public void ESMNotConfigured_MakesKktLinkUnknown()
        {
            PointStatusResult result = Result(NodeLevel.Error, EsmCashRegisterResult.Connected());
            result.EsmRegistration = EsmRegistrationResult.NotConfigured();

            DiagnosticsSnapshot snapshot = new DiagnosticsSnapshotBuilder().Create(result);

            Assert.Equal(DiagnosticConnectionState.Unknown, snapshot.EsmToKkt.State);
        }

        [Fact]
        public void EsmApiUnavailable_MakesKktLinkUnknown()
        {
            PointStatusResult result = Result(NodeLevel.Ok, EsmCashRegisterResult.Disconnected());
            result.EsmApiStatus = EsmStatusResult.Unavailable();

            Assert.Equal(DiagnosticConnectionState.Unknown, new DiagnosticsSnapshotBuilder().Create(result).EsmToKkt.State);
        }

        private static NodeStatus Build(
            KktPnpResult pnp = null,
            KktDriverProbeResult driver = null,
            NodeStatus services = null,
            KktPortProbeResult port = null,
            ComponentVersionStatus driverVersion = null) =>
            PointStatusService.BuildKktStatus(
                pnp ?? KktPnpResult.Detected("ATOL on COM42"),
                driver ?? KktDriverProbeResult.Found("x64", "10.10.8.23"),
                services ?? Services(("uem-agent", "Running"), ("uem-updater", "Running"), ("atol-grpc-service", "Running")),
                port ?? KktPortProbeResult.Available(),
                driverVersion);

        private static ComponentVersionStatus DriverVersion(string target) =>
            ComponentVersionStatus.Create("Драйвер ККТ", "10.10.8.23", target, true, ComponentVersionRequirements.MinimumSupportedAtolDriver);

        private static NodeStatus Services(params (string Name, string State)[] states) =>
            new(NodeLevel.Ok, "services", "services", Array.ConvertAll(states, item => new ServiceSnapshot(item.Name, item.State)));

        private static PointStatusResult Result(NodeLevel kktLevel, EsmCashRegisterResult cashRegister) => new()
        {
            Esm = new NodeStatus(NodeLevel.Ok, "ok", "ok"),
            Kkt = new NodeStatus(kktLevel, "kkt", "kkt"),
            Lm = new NodeStatus(NodeLevel.Ok, "lm", "lm"),
            Controller = new NodeStatus(NodeLevel.Ok, "controller", "controller"),
            EsmApiStatus = EsmStatusResult.Success(new EsmStatusDto { Gismt = new EsmComponentStatus { Code = 0 }, Lm = new EsmComponentStatus { Code = 0 }, LmInfo = new EsmLmInfoDto { Code = 0 } }),
            EsmRegistration = EsmRegistrationResult.Registered(),
            CashRegister = cashRegister,
            AtolDriverVersion = "10.10.8.23 (64-bit)"
        };
    }
}
