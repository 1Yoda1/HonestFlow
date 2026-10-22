using HonestFlow.Application.Installation;
using HonestFlow.Application.Core;
using HonestFlow.Application.Lm;
using HonestFlow.Models;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class ComponentVersionStatusTests
    {
        [Theory]
        [InlineData("2.5.1", "2.5.1", false, ComponentVersionState.Current)]
        [InlineData("2.5.0", "2.5.1", true, ComponentVersionState.UpdateRequired)]
        [InlineData("не установлен", "2.5.1", true, ComponentVersionState.NotInstalled)]
        [InlineData(null, "2.5.1", true, ComponentVersionState.NotInstalled)]
        [InlineData("2.5.1", null, null, ComponentVersionState.Unknown)]
        [InlineData("версия не определена", "2.5.1", true, ComponentVersionState.Unknown)]
        public void Create_ClassifiesVersionState(
            string installed,
            string expected,
            bool? updateRequired,
            ComponentVersionState expectedState)
        {
            ComponentVersionStatus result = ComponentVersionStatus.Create(
                "Компонент",
                installed,
                expected,
                updateRequired);

            Assert.Equal(expectedState, result.State);
        }

        [Fact]
        public void Create_SeparatesMinimumSupportedVersionFromTargetVersion()
        {
            ComponentVersionStatus supported = ComponentVersionStatus.Create(
                "Драйвер ККТ", "10.10.8.23 (64-bit)", "10.10.8.24", true, "10.10.8.23");
            ComponentVersionStatus unsupported = ComponentVersionStatus.Create(
                "Драйвер ККТ", "10.10.8.22 (64-bit)", "10.10.8.24", true, "10.10.8.23");

            Assert.Equal(ComponentVersionState.UpdateRequired, supported.State);
            Assert.Equal(ComponentVersionState.BelowMinimum, unsupported.State);
            Assert.Equal("10.10.8.23", unsupported.MinimumSupportedVersion);
            Assert.Equal("10.10.8.24", unsupported.TargetVersion);
        }

        [Theory]
        [InlineData("Regime", true)]
        [InlineData("Regime Local Module", true)]
        [InlineData("Локальный модуль Честный Знак", true)]
        [InlineData("Локальный модуль другой системы", false)]
        [InlineData("Честный Знак", false)]
        public void LmDisplayName_RecognizesSupportedUninstallNames(string displayName, bool expected)
        {
            Assert.Equal(expected, LmValidationService.IsLmUninstallDisplayName(displayName));
        }

        [Fact]
        public void StatusService_UsesClientVersionsAndClassifiesAllComponents()
        {
            var checker = new StubVersionCheckService
            {
                AtolVersion = "10.10.8.24 (64-bit)",
                EsmVersion = "1.6.3.2",
                ControllerVersion = "1.6.3.1",
                AtolNeedsUpdate = false,
                EsmNeedsUpdate = false,
                ControllerNeedsUpdate = true
            };
            var service = new ComponentVersionStatusService(checker, () => "1.0.5");
            var client = new IPData
            {
                Versions = new VersionsData
                {
                    LmModule = "1.0.5",
                    AtolDriver = "10.10.8.24",
                    ESM = "1.6.3.2",
                    Controller = "1.6.3.2"
                }
            };

            ComponentVersionStatus[] result = service.GetClientStatuses(client, new VersionsData());

            Assert.Collection(
                result,
                status => Assert.Equal(ComponentVersionState.Current, status.State),
                status => Assert.Equal(ComponentVersionState.Current, status.State),
                status => Assert.Equal(ComponentVersionState.Current, status.State),
                status => Assert.Equal(ComponentVersionState.UpdateRequired, status.State));
        }

        [Fact]
        public void StatusService_TreatsLmPackageRevisionAsSameRegistryVersion()
        {
            var service = new ComponentVersionStatusService(new StubVersionCheckService(), () => "2.6.0");
            var configured = new VersionsData { LmModule = "2.6.0-10" };

            ComponentVersionStatus status = service.GetClientStatuses(new IPData(), configured)[0];

            Assert.Equal("2.6.0", status.InstalledVersion);
            Assert.Equal("2.6.0-10", status.ExpectedVersion);
            Assert.Equal(ComponentVersionState.Current, status.State);
        }

        [Fact]
        public void StatusService_FallsBackToConfiguredVersions()
        {
            var checker = new StubVersionCheckService
            {
                AtolVersion = "10.10.8.24 (64-bit)",
                EsmVersion = "1.6.3.2",
                ControllerVersion = "1.6.3.2"
            };
            var service = new ComponentVersionStatusService(checker, () => "1.0.5");
            var configured = new VersionsData
            {
                LmModule = "1.0.5",
                AtolDriver = "10.10.8.24",
                ESM = "1.6.3.2",
                Controller = "1.6.3.2"
            };

            ComponentVersionStatus[] result = service.GetClientStatuses(new IPData(), configured);

            Assert.All(result, status => Assert.Equal(ComponentVersionState.Current, status.State));
        }

        [Fact]
        public void LocalStatuses_OnX64ReportInstalledX86AtolDriver()
        {
            var checker = new StubVersionCheckService
            {
                LocalAtolVersion = "10.10.8.24 (32-bit)",
                EsmVersion = "1.6.3.2",
                ControllerVersion = "1.6.3.2"
            };
            var service = new ComponentVersionStatusService(checker, () => "1.0.5");

            ComponentVersionStatus[] result = service.GetLocalStatuses(new LocalRuntimeContext("x64"));

            Assert.Equal("10.10.8.24 (32-bit)", result[1].InstalledVersion);
            Assert.Equal(ComponentVersionState.Installed, result[1].State);
            Assert.Null(checker.LastRequestedArchitecture);
            Assert.All(result, status => Assert.Null(status.TargetVersion));
            Assert.All(result, status => Assert.Equal(ComponentVersionState.Installed, status.State));
        }

        [Fact]
        public void LocalStatuses_OnX64ReportInstalledX64AtolDriver()
        {
            var checker = new StubVersionCheckService { LocalAtolVersion = "10.10.8.24 (64-bit)" };
            ComponentVersionStatus status = new ComponentVersionStatusService(checker, () => "1.0.5")
                .GetLocalStatuses(new LocalRuntimeContext("x64"))[1];

            Assert.Equal("10.10.8.24 (64-bit)", status.InstalledVersion);
            Assert.Equal(ComponentVersionState.Installed, status.State);
        }

        [Fact]
        public void LocalStatuses_ReportBothInstalledAtolDriverArchitectures()
        {
            var checker = new StubVersionCheckService
            {
                LocalAtolVersion = "10.10.8.24 (32-bit) и 10.10.8.25 (64-bit)"
            };
            ComponentVersionStatus status = new ComponentVersionStatusService(checker, () => "1.0.5")
                .GetLocalStatuses(new LocalRuntimeContext("x64"))[1];

            Assert.Equal("10.10.8.24 (32-bit) и 10.10.8.25 (64-bit)", status.InstalledVersion);
            Assert.Equal(ComponentVersionState.Installed, status.State);
        }

        [Fact]
        public void LocalStatuses_WithoutAtolDriverReportNotInstalled()
        {
            var checker = new StubVersionCheckService { LocalAtolVersion = "не установлен" };
            ComponentVersionStatus status = new ComponentVersionStatusService(checker, () => "1.0.5")
                .GetLocalStatuses(new LocalRuntimeContext("x64"))[1];

            Assert.Equal(ComponentVersionState.NotInstalled, status.State);
        }

        [Fact]
        public void ClientStatuses_KeepRequiredArchitectureSpecificAtolCheck()
        {
            var checker = new StubVersionCheckService
            {
                LocalAtolVersion = "10.10.8.24 (32-bit)",
                AtolVersion = "не установлен",
                AtolNeedsUpdate = true
            };
            ComponentVersionStatus status = new ComponentVersionStatusService(checker, () => "1.0.5")
                .GetClientStatuses(new IPData { Architecture = "x64", Versions = new VersionsData { AtolDriver = "10.10.8.24" } })[1];

            Assert.Equal("x64", checker.LastRequestedArchitecture);
            Assert.Equal(ComponentVersionState.NotInstalled, status.State);
        }

        [Fact]
        public void LocalStatuses_KeepAtolMinimumVersionSemantics()
        {
            var checker = new StubVersionCheckService { LocalAtolVersion = "10.10.8.22 (32-bit)" };
            ComponentVersionStatus status = new ComponentVersionStatusService(checker, () => "1.0.5")
                .GetLocalStatuses(new LocalRuntimeContext("x64"))[1];

            Assert.Equal(ComponentVersionState.BelowMinimum, status.State);
            Assert.Equal(ComponentVersionRequirements.MinimumSupportedAtolDriver, status.MinimumSupportedVersion);
        }

        [Theory]
        [InlineData("10.10.8.24 (32-bit) и 10.10.8.22 (64-bit)", ComponentVersionState.BelowMinimum)]
        [InlineData("10.10.8.22 (32-bit) и 10.10.8.24 (64-bit)", ComponentVersionState.BelowMinimum)]
        [InlineData("10.10.8.22 (64-bit) и 10.10.8.24 (32-bit)", ComponentVersionState.BelowMinimum)]
        [InlineData("10.10.8.24 (64-bit) и 10.10.8.22 (32-bit)", ComponentVersionState.BelowMinimum)]
        [InlineData("10.10.8.23 (32-bit) и 10.10.8.24 (64-bit)", ComponentVersionState.Installed)]
        [InlineData("10.10.8.24 (32-bit) и 10.10.8.23 (64-bit)", ComponentVersionState.Installed)]
        public void LocalStatuses_CheckMinimumForEveryInstalledAtolArchitecture(
            string installed, ComponentVersionState expected)
        {
            var checker = new StubVersionCheckService { LocalAtolVersion = installed };
            ComponentVersionStatus status = new ComponentVersionStatusService(checker, () => "1.0.5")
                .GetLocalStatuses(new LocalRuntimeContext("x64"))[1];

            Assert.Equal(expected, status.State);
            Assert.Equal(installed, status.InstalledVersion);
            Assert.Null(status.TargetVersion);
        }

        private sealed class StubVersionCheckService : IVersionCheckService
        {
            public string AtolVersion { get; init; }
            public string LocalAtolVersion { get; init; }
            public string EsmVersion { get; init; }
            public string ControllerVersion { get; init; }
            public bool AtolNeedsUpdate { get; init; }
            public bool EsmNeedsUpdate { get; init; }
            public bool ControllerNeedsUpdate { get; init; }
            public string LastRequestedArchitecture { get; private set; }

            public bool NeedAtolInstall(IPData selectedIP, string expectedVersion) => AtolNeedsUpdate;
            public bool NeedEsmInstall(string expectedVersion) => EsmNeedsUpdate;
            public bool NeedControllerInstall(string expectedVersion) => ControllerNeedsUpdate;
            public string GetAtolDriverInfo() => LocalAtolVersion;
            public string GetAtolDriverInfo(string requiredArchitecture)
            {
                LastRequestedArchitecture = requiredArchitecture;
                return AtolVersion;
            }
            public string GetEsmVersion() => EsmVersion;
            public string GetControllerVersion() => ControllerVersion;
        }
    }
}
