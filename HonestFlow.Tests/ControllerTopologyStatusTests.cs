using System;
using HonestFlow.Application.Installation;
using HonestFlow.Application.PointStatus;
using Xunit;

namespace HonestFlow.Tests;

public sealed class ControllerTopologyStatusTests
{
    [Fact]
    public void NotInstalled_IsRed()
    {
        NodeStatus result = Build(MissingService(), Available(), Version(ComponentVersionState.NotInstalled));

        Assert.Equal(NodeLevel.Error, result.Level);
        Assert.Equal("Контроллер не установлен", result.StatusText);
    }

    [Fact]
    public void Installed_ServiceMissing_IsRed()
    {
        NodeStatus result = Build(MissingService(), Available(), Version(ComponentVersionState.Current));

        Assert.Equal(NodeLevel.Error, result.Level);
        Assert.Equal("Служба не найдена", result.StatusText);
    }

    [Fact]
    public void Installed_ServiceStopped_IsRed()
    {
        NodeStatus result = Build(Service("Stopped"), Available(), Version(ComponentVersionState.Current));

        Assert.Equal(NodeLevel.Error, result.Level);
        Assert.Equal("Служба остановлена", result.StatusText);
    }

    [Theory]
    [InlineData("connection_refused")]
    [InlineData("timeout")]
    public void Installed_RunningService_ServiceInfoUnavailable_IsRed(string errorCategory)
    {
        NodeStatus result = Build(Service("Running"), Unavailable(errorCategory), Version(ComponentVersionState.Current));

        Assert.Equal(NodeLevel.Error, result.Level);
        Assert.Equal("Контроллер недоступен", result.StatusText);
    }

    [Fact]
    public void Installed_RunningService_CurrentServiceInfo_VersionMismatch_IsYellowWithTwoLines()
    {
        NodeStatus result = Build(Service("Running"), Available(), Version(ComponentVersionState.UpdateRequired));

        Assert.Equal(NodeLevel.Warning, result.Level);
        Assert.Equal("Установите последнюю\nверсию", result.StatusText);
    }

    [Fact]
    public void Installed_RunningService_CurrentServiceInfo_CurrentVersion_IsGreen()
    {
        NodeStatus result = Build(Service("Running"), Available(), Version(ComponentVersionState.Current));

        Assert.Equal(NodeLevel.Ok, result.Level);
        Assert.Equal("Доступно", result.StatusText);
    }

    [Fact]
    public void VersionUndetermined_IsYellowWhenOtherControllerChecksPass()
    {
        NodeStatus result = Build(Service("Running"), Available(), Version(ComponentVersionState.Unknown));

        Assert.Equal(NodeLevel.Warning, result.Level);
        Assert.Equal("Версия не определена", result.StatusText);
    }

    [Fact]
    public void ControllerTargetVersionMismatch_IsAttentionNotWorkImpossible()
    {
        NodeStatus controllerService = Service("Running");
        PointStatusResult result = new()
        {
            Controller = Build(controllerService, Available(), Version(ComponentVersionState.UpdateRequired)),
            ControllerServiceStatus = controllerService,
            ControllerServiceInfo = Available(),
            Esm = new NodeStatus(NodeLevel.Ok, "OK", "OK", new[] { new ServiceSnapshot("esm-orchestrator", "Running") }),
            Kkt = new NodeStatus(NodeLevel.Ok, "OK", "OK", new[] { new ServiceSnapshot("atol-grpc-service", "Running") }),
            Lm = new NodeStatus(NodeLevel.Ok, "OK", "OK", new[] { new ServiceSnapshot("regime", "Running") }),
            EsmRegistration = EsmRegistrationResult.Registered(),
            CashRegister = EsmCashRegisterResult.Connected(),
            KktPnP = KktPnpResult.Detected("ATOL 30F"),
            AtolDriverVersion = "10.10.8.23 (64-bit)",
            EsmApiStatus = EsmStatusResult.Success(new EsmStatusDto
            {
                Gismt = Code(0), LmInfo = new EsmLmInfoDto { Code = 0 }, Lm = Code(0)
            })
        };

        DiagnosticsSnapshot snapshot = new DiagnosticsSnapshotBuilder().Create(result,
            new[] { Version(ComponentVersionState.UpdateRequired) });

        Assert.Equal(DiagnosticState.Unknown, snapshot.Controller.State);
        Assert.Equal(WorkState.Attention, snapshot.WorkState);
    }

    [Fact]
    public void BadControllerToLmLink_DoesNotChangeControllerNode()
    {
        DiagnosticsSnapshot snapshot = Snapshot(lmControllerCode: 0, lmCode: 5);
        TopologyPresentation presentation = new TopologyPresentationService().Create(snapshot);

        Assert.Equal(TopologyVisualState.Healthy, presentation.ControllerFrame);
        Assert.Equal(TopologyVisualState.Missing, presentation.ControllerToLm.State);
    }

    [Fact]
    public void BadEsmToControllerLink_DoesNotChangeControllerNode()
    {
        DiagnosticsSnapshot snapshot = Snapshot(lmControllerCode: 3, lmCode: 0);
        TopologyPresentation presentation = new TopologyPresentationService().Create(snapshot);

        Assert.Equal(TopologyVisualState.Healthy, presentation.ControllerFrame);
        Assert.Equal(TopologyVisualState.Missing, presentation.EsmToController.State);
    }

    [Fact]
    public void MissingEsmControllerCode_IsYellowWithoutChangingControllerNode()
    {
        DiagnosticsSnapshot snapshot = Snapshot(lmControllerCode: null, lmCode: 0);
        TopologyPresentation presentation = new TopologyPresentationService().Create(snapshot);

        Assert.Equal(TopologyVisualState.Healthy, presentation.ControllerFrame);
        Assert.Equal(TopologyVisualState.Uncertain, presentation.EsmToController.State);
    }

    [Fact]
    public void DeveloperDiagnostics_KeepControllerAndEsmLinkFactsSeparate()
    {
        DiagnosticsSnapshot snapshot = Snapshot(lmControllerCode: 3, lmCode: 0);

        Assert.Contains(snapshot.Facts, fact => fact.Key == "Controller.Installed");
        Assert.Contains(snapshot.Facts, fact => fact.Key == "Controller.InstalledVersion");
        Assert.Contains(snapshot.Facts, fact => fact.Key == "Controller.TargetVersion");
        Assert.Contains(snapshot.Facts, fact => fact.Key == "Controller.VersionMatch");
        Assert.Contains(snapshot.Facts, fact => fact.Key == "Controller.ServiceExists");
        Assert.Contains(snapshot.Facts, fact => fact.Key == "Controller.ServiceRunning");
        Assert.Contains(snapshot.Facts, fact => fact.Key == "Controller.ServiceInfoAvailable");
        Assert.Contains(snapshot.Facts, fact => fact.Key == "Controller.ServiceInfoHttpStatus");
        Assert.Contains(snapshot.Facts, fact => fact.Key == "Controller.ServiceInfoErrorCategory");
        Assert.Contains(snapshot.Facts, fact => fact.Key == "EsmApiPort");
        Assert.Contains(snapshot.Facts, fact => fact.Key == "EsmToController.LmDataCode" && fact.Value == "3");
        Assert.Contains(snapshot.Facts, fact => fact.Key == "EsmToController.LinkState" && fact.Value == "Disconnected");
    }

    [Fact]
    public void RunningService_NeverReportsStoppedBecauseOfBadLinks()
    {
        NodeStatus controller = Build(Service("Running"), Available(), Version(ComponentVersionState.Current));

        Assert.Equal("Доступно", controller.StatusText);
        Assert.DoesNotContain("остановлена", controller.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    private static NodeStatus Build(NodeStatus service, ControllerServiceInfoResult info, ComponentVersionStatus version) =>
        PointStatusService.BuildControllerStatus(service, info, version);

    private static NodeStatus MissingService() => new(NodeLevel.Error, "Нет службы", "Не найдена");
    private static NodeStatus Service(string state) => new(NodeLevel.Ok, state, state,
        new[] { new ServiceSnapshot("esm-lm-controller", state) });
    private static ControllerServiceInfoResult Available() => ControllerServiceInfoResult.Available(200);
    private static ControllerServiceInfoResult Unavailable(string category) => ControllerServiceInfoResult.Unavailable(category);
    private static ComponentVersionStatus Version(ComponentVersionState state) =>
        new("Контроллер", state == ComponentVersionState.NotInstalled ? null : "1.6.3.1", "1.6.3.2", state);

    private static DiagnosticsSnapshot Snapshot(int? lmControllerCode, int lmCode)
    {
        NodeStatus controllerService = Service("Running");
        PointStatusResult result = new()
        {
            Controller = Build(controllerService, Available(), Version(ComponentVersionState.Current)),
            ControllerServiceStatus = controllerService,
            ControllerServiceInfo = Available(),
            Esm = new NodeStatus(NodeLevel.Ok, "OK", "OK"),
            Kkt = new NodeStatus(NodeLevel.Ok, "OK", "OK"),
            Lm = new NodeStatus(NodeLevel.Ok, "OK", "OK"),
            EsmRegistration = EsmRegistrationResult.Registered(),
            CashRegister = EsmCashRegisterResult.Connected(),
            KktPnP = KktPnpResult.Detected("ATOL 30F"),
            AtolDriverVersion = "10.10.8.23 (64-bit)",
            EsmApiStatus = EsmStatusResult.Success(new EsmStatusDto
            {
                Gismt = Code(0),
                LmInfo = new EsmLmInfoDto { Code = lmControllerCode },
                Lm = Code(lmCode)
            }, 51077)
        };
        return new DiagnosticsSnapshotBuilder().Create(result, new[] { Version(ComponentVersionState.Current) });
    }

    private static EsmComponentStatus Code(int code) => new() { Code = code };
}
