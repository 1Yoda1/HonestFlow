using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using HonestFlow.Application.PointStatus;
using Xunit;

namespace HonestFlow.Tests;

public sealed class CompactWorkplacePresentationTests
{
    [Fact]
    public void WorkImpossible_UsesRequiredUserTextWithoutForbiddenWording()
    {
        CompactWorkplacePresentation presentation = Create(WorkState.WorkImpossible);

        Assert.Equal("Работа невозможна", presentation.Title);
        Assert.DoesNotContain("Работать нельзя", presentation.Title + presentation.Description);
        Assert.True(presentation.CanAutoFix);
    }

    [Fact]
    public void Ready_AttentionAndUnableToVerify_UseCompactUserText()
    {
        Assert.Equal("Работа возможна", Create(WorkState.Ready).Title);
        Assert.Equal("Работа возможна", Create(WorkState.Attention).Title);
        Assert.Contains("не мешающие работе", Create(WorkState.Attention).Description);
        Assert.Equal("Работа не подтверждена", Create(WorkState.UnableToVerify).Title);
    }

    [Fact]
    public void CloudState_UsesExistingConnectivityProbeResult_NotConfigurationMode()
    {
        DiagnosticsSnapshot snapshot = Snapshot(WorkState.Ready);

        Assert.Equal("Облако доступно", CompactWorkplacePresentation.Create(
            snapshot, HonestFlowCloudStatus.Available).CloudStatus);
        Assert.Equal("Облако недоступно", CompactWorkplacePresentation.Create(
            snapshot, HonestFlowCloudStatus.Unavailable).CloudStatus);
        Assert.Equal("Облако: состояние неизвестно", CompactWorkplacePresentation.Create(
            snapshot, HonestFlowCloudStatus.Unknown).CloudStatus);
    }

    [Fact]
    public void CompactUi_HasSmallScreenContractAndNoTechnicalViews()
    {
        string xaml = File.ReadAllText(ProjectFile("HonestFlow.WpfPrototype", "CompactMainWindow.xaml"));

        Assert.Contains("Width=\"540\"", xaml);
        Assert.Contains("MaxWidth=\"560\"", xaml);
        Assert.Contains("DeviceIdText", xaml);
        Assert.Contains("CopyDeviceIdButton", xaml);
        Assert.Contains("CloudStatusText", xaml);
        Assert.Contains("CompactPrimaryButton", xaml);
        Assert.Contains("CompactSecondaryButton", xaml);
        Assert.Contains("CompactIconButton", xaml);
        Assert.Contains("ToolTip=\"Скопировать Device ID\"", xaml);
        Assert.DoesNotContain("Content=\"Копировать\"", xaml);
        Assert.DoesNotContain("Topology", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ComponentVersions", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TechnicalDetails", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DeveloperDiagnostics", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompactCloud_UsesAuthorizedHonestFlowApiConfigurationRequest()
    {
        string compactCode = File.ReadAllText(ProjectFile("HonestFlow.WpfPrototype", "CompactMainWindow.xaml.cs"));
        string probeCode = File.ReadAllText(ProjectFile("Infrastructure", "Api", "ApiServerConnectivityProbe.cs"));

        Assert.Contains("ApiServerConnectivityProbe", compactCode);
        Assert.Contains("api/configuration/current", probeCode);
        Assert.DoesNotContain("cloud-api.yandex.net", compactCode, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompactActions_ConfirmAndRunSharedAutoFixWorkflowLocally()
    {
        string compactCode = File.ReadAllText(ProjectFile("HonestFlow.WpfPrototype", "CompactMainWindow.xaml.cs"));
        string fullCode = File.ReadAllText(ProjectFile("HonestFlow.WpfPrototype", "MainWindow.xaml.cs"));

        Assert.Contains("MessageBoxButton.YesNo", compactCode);
        Assert.Contains("WpfAutoFixComposition.Create", compactCode);
        Assert.Contains("_autoFixWorkflow.RunAsync", compactCode);
        Assert.Contains("CompactAutoFixOverlay", File.ReadAllText(ProjectFile("HonestFlow.WpfPrototype", "CompactMainWindow.xaml")));
        Assert.Contains("new MainWindow(_session.Startup, _client, _license)", compactCode);
        Assert.DoesNotContain("fullWindow.StartAutoFixAsync", compactCode);
        Assert.Contains("WindowState = WindowState.Maximized", compactCode);
        Assert.Contains("FullWindow_Closed", compactCode);
        Assert.Contains("await StartVisibleWorkAsync()", compactCode);
        Assert.Contains("public async Task StartAutoFixAsync()", fullCode);
    }

    [Theory]
    [InlineData(WorkState.Ready, CompactWorkplaceVisualState.Ready, "#137A4B")]
    [InlineData(WorkState.Attention, CompactWorkplaceVisualState.Attention, "#B7791F")]
    [InlineData(WorkState.UnableToVerify, CompactWorkplaceVisualState.UnableToVerify, "#C66A12")]
    [InlineData(WorkState.WorkImpossible, CompactWorkplaceVisualState.WorkImpossible, "#C6283D")]
    public void WorkState_UsesExpectedCompactVisualPresentation(
        WorkState state,
        CompactWorkplaceVisualState expectedVisualState,
        string expectedAccentColor)
    {
        CompactWorkplacePresentation presentation = Create(state);

        Assert.Equal(expectedVisualState, presentation.VisualState);
        Assert.Equal(expectedAccentColor, presentation.AccentColor);
        Assert.False(string.IsNullOrWhiteSpace(presentation.TintColor));
        Assert.False(string.IsNullOrWhiteSpace(presentation.BorderColor));
    }

    private static CompactWorkplacePresentation Create(WorkState state) =>
        CompactWorkplacePresentation.Create(Snapshot(state), HonestFlowCloudStatus.Available);

    private static DiagnosticsSnapshot Snapshot(WorkState state) => new()
    {
        WorkState = state,
        ObservedAtUtc = DateTimeOffset.UtcNow
    };

    private static string ProjectFile(params string[] parts) => Path.GetFullPath(Path.Combine(
        new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(parts).ToArray()));
}
