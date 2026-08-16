using HonestFlow.Application.PointStatus;
using HonestFlow.Application.Installation;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class DiagnosticsSnapshotBuilderTests
    {
        private readonly DiagnosticsSnapshotBuilder _builder = new();

        [Fact]
        public void EsmApiUnavailable_MakesEsmFailed_WorkImpossible_AndDependentLinksUnknown()
        {
            PointStatusResult result = Healthy();
            result.EsmApiStatus = EsmStatusResult.Unavailable();

            DiagnosticsSnapshot snapshot = _builder.Create(result);

            Assert.Equal(DiagnosticState.Failed, snapshot.Esm.State);
            Assert.Equal(WorkState.WorkImpossible, snapshot.WorkState);
            Assert.Equal(DiagnosticConnectionState.Unknown, snapshot.EsmToKkt.State);
            Assert.Equal(DiagnosticConnectionState.Unknown, snapshot.EsmToController.State);
        }

        [Fact]
        public void UnregisteredEsm_MakesWorkImpossible()
        {
            PointStatusResult result = Healthy();
            result.EsmRegistration = EsmRegistrationResult.NotConfigured();
            Assert.Equal(WorkState.WorkImpossible, _builder.Create(result).WorkState);
        }

        [Theory]
        [InlineData("10.10.8.22")]
        [InlineData("не установлен")]
        public void OldOrMissingAtolDriver_MakesKktFailed_WorkImpossible(string version)
        {
            PointStatusResult result = Healthy();
            result.AtolDriverVersion = version;
            DiagnosticsSnapshot snapshot = _builder.Create(result);
            Assert.Equal(DiagnosticState.Failed, snapshot.Kkt.State);
            Assert.Equal(WorkState.WorkImpossible, snapshot.WorkState);
        }

        [Fact]
        public void StoppedAtolService_MakesWorkImpossible()
        {
            PointStatusResult result = Healthy();
            result.Kkt = Node(NodeLevel.Error, "atol-grpc-service", "Stopped");
            Assert.Equal(WorkState.WorkImpossible, _builder.Create(result).WorkState);
        }

        [Fact]
        public void EmptyCashRegister_MakesEsmKktDisconnected_WorkImpossible()
        {
            PointStatusResult result = Healthy();
            result.CashRegister = EsmCashRegisterResult.Disconnected();
            DiagnosticsSnapshot snapshot = _builder.Create(result);
            Assert.Equal(DiagnosticConnectionState.Disconnected, snapshot.EsmToKkt.State);
            Assert.Equal(WorkState.WorkImpossible, snapshot.WorkState);
        }

        [Fact]
        public void GismtCodes_MapToAvailableUnavailableAndUnknown()
        {
            Assert.Equal(DiagnosticState.Healthy, _builder.Create(Healthy()).Gismt.State);
            PointStatusResult unavailable = Healthy(); unavailable.EsmApiStatus.Status.Gismt.Code = 12;
            Assert.Equal(DiagnosticState.Failed, _builder.Create(unavailable).Gismt.State);
            PointStatusResult unknown = Healthy(); unknown.EsmApiStatus.Status.Gismt = null;
            Assert.Equal(DiagnosticState.Unknown, _builder.Create(unknown).Gismt.State);
        }

        [Fact]
        public void NestedDataEnvelope_IsUsedForGismtFact()
        {
            PointStatusResult result = Healthy();
            result.EsmApiStatus = EsmStatusResult.Success(new EsmStatusDto
            {
                Data = new EsmStatusDto { ClientSoftware = Code(0), Gismt = Code(0), LmController = Code(0), Lm = Code(0) }
            });

            DiagnosticsSnapshot snapshot = _builder.Create(result);

            Assert.Equal(DiagnosticState.Healthy, snapshot.Gismt.State);
        }

        [Fact]
        public void LmAndGismtBothDown_MakesWorkImpossible_ButEitherOneAvailableDoesNot()
        {
            PointStatusResult bothDown = Healthy();
            bothDown.Lm = Node(NodeLevel.Error, "regime", "Stopped");
            bothDown.EsmApiStatus.Status.Gismt.Code = 1;
            Assert.Equal(WorkState.WorkImpossible, _builder.Create(bothDown).WorkState);

            PointStatusResult gisUp = Healthy(); gisUp.Lm = Node(NodeLevel.Error, "regime", "Stopped");
            Assert.NotEqual(WorkState.WorkImpossible, _builder.Create(gisUp).WorkState);
        }

        [Fact]
        public void LmDownAndGismtUnknown_HasNoAvailableMarkingPath()
        {
            PointStatusResult result = Healthy();
            result.Lm = Node(NodeLevel.Error, "regime", "Stopped");
            result.EsmApiStatus.Status.Gismt = null;
            Assert.Equal(WorkState.WorkImpossible, _builder.Create(result).WorkState);
        }

        [Fact]
        public void ControllerDown_IsFailedButDoesNotMakeWorkImpossible()
        {
            PointStatusResult result = Healthy();
            result.Controller = Node(NodeLevel.Error, "esm-lm-controller", "Stopped");
            DiagnosticsSnapshot snapshot = _builder.Create(result);
            Assert.Equal(DiagnosticState.Failed, snapshot.Controller.State);
            Assert.NotEqual(WorkState.WorkImpossible, snapshot.WorkState);
        }

        [Fact]
        public void LmConnections_MapFromTheirOwnCodes_WithoutClientSoftwareHealth()
        {
            PointStatusResult result = Healthy();
            result.EsmApiStatus.Status.LmController.Code = 4;
            result.EsmApiStatus.Status.Lm.Code = 5;
            DiagnosticsSnapshot snapshot = _builder.Create(result);
            Assert.Equal(DiagnosticConnectionState.Disconnected, snapshot.EsmToController.State);
            Assert.Equal(DiagnosticConnectionState.Disconnected, snapshot.LmConnection.State);
        }

        [Fact]
        public void ClientSoftwareMissing_DoesNotAffectWorkState()
        {
            PointStatusResult result = Healthy();
            result.EsmApiStatus.Status.ClientSoftware = null;

            DiagnosticsSnapshot snapshot = _builder.Create(result);

            Assert.Equal(WorkState.Ready, snapshot.WorkState);
            Assert.DoesNotContain(snapshot.Issues, issue => issue.Component == DiagnosticComponent.ClientSoftware);
        }

        [Fact]
        public void ClientSoftwareMetadata_RemainsAnInformationalDeveloperFact()
        {
            PointStatusResult result = Healthy();
            result.EsmApiStatus.Status.ClientSoftware = new EsmComponentStatus
            {
                Code = 0,
                Version = "8.3.25",
                Id = "pos-17"
            };

            DiagnosticsSnapshot snapshot = _builder.Create(result);

            DiagnosticFact fact = Assert.Single(snapshot.Facts, item => item.Key == "ClientSoftware");
            Assert.Equal(DiagnosticFactState.Unknown, fact.State);
            Assert.Contains("version=8.3.25", fact.Evidence);
            Assert.Contains("id=pos-17", fact.Evidence);
        }

        [Fact]
        public void ClientSoftwareError_DoesNotAffectWorkState()
        {
            PointStatusResult result = Healthy();
            result.EsmApiStatus.Status.ClientSoftware.Code = 7;

            DiagnosticsSnapshot snapshot = _builder.Create(result);

            Assert.Equal(WorkState.Ready, snapshot.WorkState);
            Assert.DoesNotContain(snapshot.Issues, issue => issue.Component == DiagnosticComponent.ClientSoftware);
        }

        [Fact]
        public void SoftwareDataEnvelope_IsPreferred()
        {
            PointStatusResult result = Healthy();
            result.EsmApiStatus = EsmStatusResult.Success(new EsmStatusDto
            {
                ClientSoftware = null,
                Software = new EsmSoftwareStatusDto
                {
                    Data = new EsmStatusDto
                    {
                        ClientSoftware = Code(0),
                        Gismt = Code(0),
                        LmController = Code(0),
                        Lm = Code(0)
                    }
                }
            });

            DiagnosticsSnapshot snapshot = _builder.Create(result);

            Assert.Equal(WorkState.Ready, snapshot.WorkState);
        }

        [Fact]
        public void SupportedButDifferentFromTarget_IsAttention()
        {
            PointStatusResult result = Healthy();
            ComponentVersionStatus version = ComponentVersionStatus.Create(
                "Драйвер ККТ", "10.10.8.23 (64-bit)", "10.10.8.24", true,
                ComponentVersionRequirements.MinimumSupportedAtolDriver);

            DiagnosticsSnapshot snapshot = _builder.Create(result, new[] { version });

            Assert.Equal(ComponentVersionState.UpdateRequired, version.State);
            Assert.Equal(WorkState.Attention, snapshot.WorkState);
        }

        [Fact]
        public void BelowHardMinimum_IsWorkImpossible()
        {
            PointStatusResult result = Healthy();
            result.AtolDriverVersion = "10.10.8.22 (64-bit)";
            ComponentVersionStatus version = ComponentVersionStatus.Create(
                "Драйвер ККТ", result.AtolDriverVersion, "10.10.8.24", true,
                ComponentVersionRequirements.MinimumSupportedAtolDriver);

            DiagnosticsSnapshot snapshot = _builder.Create(result, new[] { version });

            Assert.Equal(ComponentVersionState.BelowMinimum, version.State);
            Assert.Equal(WorkState.WorkImpossible, snapshot.WorkState);
        }

        [Theory]
        [InlineData(NodeLevel.Error)]
        [InlineData(NodeLevel.Warning)]
        public void GisAvailable_AndLmDegraded_IsAttention(NodeLevel lmLevel)
        {
            PointStatusResult result = Healthy();
            result.Lm = Node(lmLevel, "regime", "sync_error");

            DiagnosticsSnapshot snapshot = _builder.Create(result);

            Assert.True(snapshot.GisPathAvailable);
            Assert.False(snapshot.LmPathAvailable);
            Assert.Equal(WorkState.Attention, snapshot.WorkState);
            Assert.Contains("ЛМ ЧЗ недоступен. Проверка выполняется через ГИС МТ.", snapshot.UserMessages);
        }

        [Fact]
        public void GisAvailable_AndLmAbsent_IsAttention()
        {
            PointStatusResult result = Healthy();
            result.Lm = null;

            DiagnosticsSnapshot snapshot = _builder.Create(result);

            Assert.Equal(WorkState.Attention, snapshot.WorkState);
            Assert.Contains("ЛМ ЧЗ недоступен. Проверка выполняется через ГИС МТ.", snapshot.UserMessages);
        }

        [Fact]
        public void GisUnavailable_AndHealthyLmPath_IsAttention()
        {
            PointStatusResult result = Healthy();
            result.EsmApiStatus.Status.Gismt.Code = 5;

            DiagnosticsSnapshot snapshot = _builder.Create(result);

            Assert.False(snapshot.GisPathAvailable);
            Assert.True(snapshot.LmPathAvailable);
            Assert.Equal(WorkState.Attention, snapshot.WorkState);
            Assert.Contains("ГИС МТ недоступна. Проверка выполняется через ЛМ ЧЗ.", snapshot.UserMessages);
        }

        [Fact]
        public void GisUnavailable_AndLocalLmHealthyButLmPathDisconnected_IsWorkImpossible()
        {
            PointStatusResult result = Healthy();
            result.EsmApiStatus.Status.Gismt.Code = 5;
            result.EsmApiStatus.Status.Lm.Code = 9;

            DiagnosticsSnapshot snapshot = _builder.Create(result);

            Assert.Equal(DiagnosticState.Healthy, snapshot.Lm.State);
            Assert.False(snapshot.LmPathAvailable);
            Assert.Equal(WorkState.WorkImpossible, snapshot.WorkState);
        }

        [Fact]
        public void ControllerLmDisconnected_AndGisAvailable_IsAttention()
        {
            PointStatusResult result = Healthy();
            result.EsmApiStatus.Status.LmController.Code = 3;

            DiagnosticsSnapshot snapshot = _builder.Create(result);

            Assert.Equal(WorkState.Attention, snapshot.WorkState);
            Assert.Contains("Контроллер не видит ЛМ ЧЗ.", snapshot.UserMessages);
        }

        [Fact]
        public void LiveKktConnection_WinsWhenAtolPnpIsNotFound()
        {
            PointStatusResult result = Healthy();
            result.KktPnP = KktPnpResult.NotDetected();

            DiagnosticsSnapshot snapshot = _builder.Create(result);

            Assert.Equal(DiagnosticConnectionState.Connected, snapshot.EsmToKkt.State);
            Assert.DoesNotContain("ККТ не обнаружена.", snapshot.UserMessages);
        }

        [Fact]
        public void EsmDoesNotSeeKkt_ButAtolPnpPresent_IsWorkImpossibleWithSpecificDiagnosis()
        {
            PointStatusResult result = Healthy();
            result.CashRegister = EsmCashRegisterResult.Disconnected();
            result.KktPnP = KktPnpResult.Detected("ATOL 30F");

            DiagnosticsSnapshot snapshot = _builder.Create(result);

            Assert.Equal(WorkState.WorkImpossible, snapshot.WorkState);
            Assert.Contains("ККТ обнаружена Windows, но ТС ПИоТ её не видит.", snapshot.UserMessages);
        }

        [Fact]
        public void NeitherEsmNorWindowsSeesKkt_IsWorkImpossibleWithSpecificDiagnosis()
        {
            PointStatusResult result = Healthy();
            result.CashRegister = EsmCashRegisterResult.Disconnected();
            result.KktPnP = KktPnpResult.NotDetected();

            DiagnosticsSnapshot snapshot = _builder.Create(result);

            Assert.Equal(WorkState.WorkImpossible, snapshot.WorkState);
            Assert.Contains("ККТ не обнаружена.", snapshot.UserMessages);
        }

        [Theory]
        [InlineData("atol usb device", null, null)]
        [InlineData(null, "ККТ ATOL 30Ф", null)]
        [InlineData(null, null, "AtOl")]
        public void KktPnpMatcher_IsCaseInsensitiveAcrossSupportedProperties(
            string name,
            string friendlyName,
            string manufacturer)
        {
            Assert.True(KktPnpDeviceMatcher.IsAtol(name, friendlyName, manufacturer));
        }

        private static PointStatusResult Healthy() => new()
        {
            Esm = Node(NodeLevel.Ok, "esm-orchestrator", "Running"),
            Kkt = Node(NodeLevel.Ok, "atol-grpc-service", "Running"),
            Lm = Node(NodeLevel.Ok, "regime", "Running"),
            Controller = Node(NodeLevel.Ok, "esm-lm-controller", "Running"),
            EsmRegistration = EsmRegistrationResult.Registered(),
            CashRegister = EsmCashRegisterResult.Connected(),
            KktPnP = KktPnpResult.NotDetected(),
            AtolDriverVersion = "10.10.8.23 (64-bit)",
            EsmApiStatus = EsmStatusResult.Success(new EsmStatusDto
            {
                ClientSoftware = Code(0), Gismt = Code(0), LmController = Code(0), Lm = Code(0)
            })
        };

        private static EsmComponentStatus Code(int code) => new() { Code = code, Name = "component" };
        private static NodeStatus Node(NodeLevel level, string service, string state) =>
            new(level, level.ToString(), service + ": " + state, new[] { new ServiceSnapshot(service, state) });
    }
}
