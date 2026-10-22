using System;
using System.IO;
using Xunit;

namespace HonestFlow.Tests;

public sealed class MainWindowFreeServicePresentationTests
{
    [Fact]
    public void RuntimeTransition_RefreshesTheOpenSectionWithoutReplacingTheWindow()
    {
        string source = ReadProjectFile("UI", "MainWindow.xaml.cs");
        string activation = Segment(source, "internal async Task ActivateServiceAsync", "private async Task DeactivateServiceAsync");
        string deactivation = Segment(source, "private async Task DeactivateServiceAsync", "private async Task RefreshCurrentSectionAfterRuntimeChangeAsync");
        string refresh = Segment(source, "private async Task RefreshCurrentSectionAfterRuntimeChangeAsync", "private void ShowToolError");

        Assert.Contains("await RefreshCurrentSectionAfterRuntimeChangeAsync();", activation);
        Assert.Contains("await RefreshCurrentSectionAfterRuntimeChangeAsync();", deactivation);
        Assert.DoesNotContain("new MainWindow", activation);
        Assert.Contains("case Section.Home:", refresh);
        Assert.Contains("await RefreshTopologyAsync();", refresh);
        Assert.Contains("case Section.Diagnostics:", refresh);
        Assert.Contains("await RefreshServiceToolsAsync();", refresh);
        Assert.Contains("case Section.Components:", refresh);
        Assert.Contains("await ShowComponentsAsync();", refresh);
        Assert.Contains("case Section.Log:", refresh);
        Assert.Contains("UpdateLiveLog(force: true);", refresh);
    }

    [Fact]
    public void ToolsPresentation_ShowsAllServiceActionsAndHidesThemInFree()
    {
        string source = ReadProjectFile("UI", "MainWindow.xaml.cs");
        string free = Segment(source, "private void ApplyFreePresentation()", "private void ApplyServicePresentation()");
        string service = Segment(source, "private void ApplyServicePresentation()", "private void ShowServiceConnectionStatus");

        foreach (string control in new[]
        {
            "SendDiagnosticsButton", "ServiceControlColumn", "RemoveEsmGuiDuplicatesButton",
            "AdminCommandPromptButton", "AdminPowerShellButton"
        })
        {
            Assert.Contains($"{control}.Visibility = Visibility.Collapsed;", free);
            Assert.Contains($"{control}.Visibility = Visibility.Visible;", service);
            Assert.DoesNotContain($"{control}.Visibility = Visibility.Collapsed;", service);
        }
    }

    [Fact]
    public void ComponentsSection_RebuildsRowsAndActionsForTheCurrentRuntime()
    {
        string source = ReadProjectFile("UI", "MainWindow.xaml.cs");
        string section = Segment(source, "private async Task ShowComponentsAsync()", "private void LogNav_Click");
        string rows = Segment(source, "private void PopulateComponentVersions()", "private async void UpdateComponent_Click");

        Assert.Contains("_paidExecutionEnabled ? \"Обновить всё\" : null", section);
        Assert.Contains("PopulateComponentLoadingRows();", section);
        Assert.Contains("await LoadComponentVersionsAsync();", section);
        Assert.Contains("CanUpdate = _paidExecutionEnabled", rows);
    }

    [Fact]
    public void ServiceHeader_ShowsDeviceIdAndNeverClientInn()
    {
        string source = ReadProjectFile("UI", "MainWindow.xaml.cs");
        string presentation = Segment(source, "private void ApplyServicePresentation()", "private void ShowServiceConnectionStatus");

        Assert.Contains("$\"Device ID: {_deviceId}\"", presentation);
        Assert.Contains("CopyDeviceIdButton.IsEnabled = !string.IsNullOrWhiteSpace(_deviceId);", presentation);
        Assert.DoesNotContain("_client?.Inn", presentation);
    }

    [Fact]
    public void ComponentReinstall_FreeIsDisabled_AndServiceEitherRunsWorkflowOrShowsSpecificError()
    {
        string source = ReadProjectFile("UI", "MainWindow.xaml.cs");
        string handler = Segment(source, "private async void UpdateComponent_Click", "private async Task ReinstallSelectedAsync");

        Assert.Contains("if (!_paidExecutionEnabled)", handler);
        Assert.Contains("Service-сессия не инициализирована", handler);
        Assert.Contains("RunInstallationOperationAsync(", handler);
        Assert.Contains("workflow.ReinstallAsync", handler);
        Assert.DoesNotContain("if (!_paidExecutionEnabled || _client == null", handler);
    }

    [Fact]
    public void MainWindowLayout_RemovesHeaderStatusAndKeepsServiceControlsOutOfFreeMode()
    {
        string xaml = ReadProjectFile("UI", "MainWindow.xaml");
        string source = ReadProjectFile("UI", "MainWindow.xaml.cs");
        string free = Segment(source, "private void ApplyFreePresentation()", "private void ApplyServicePresentation()");
        string service = Segment(source, "private void ApplyServicePresentation()", "private void ShowServiceConnectionStatus");

        Assert.DoesNotContain("HeaderStatusBorder", xaml);
        Assert.DoesNotContain("HeaderStatusBorder", source);
        Assert.Contains("ServiceControlColumn.Visibility = Visibility.Collapsed", free);
        Assert.Contains("ServiceControlColumn.Visibility = Visibility.Visible", service);
        Assert.Contains("x:Name=\"ServiceControlColumn\" Width=\"355\"", xaml);
        Assert.Contains("<WrapPanel VerticalAlignment=\"Center\">", xaml);
    }

    private static string Segment(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Marker not found: {startMarker}");
        Assert.True(end > start, $"Marker not found after start: {endMarker}");
        return source.Substring(start, end - start);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        string path = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8; depth++)
        {
            string candidate = Path.Combine(path, Path.Combine(parts));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            path = Directory.GetParent(path)?.FullName ?? path;
        }

        throw new FileNotFoundException("Project file not found", Path.Combine(parts));
    }
}
