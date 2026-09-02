using System;
using System.IO;
using Xunit;

namespace HonestFlow.Tests;

public sealed class InstructionWindowTests
{
    [Fact]
    public void MainWindow_UpdatesInstructionAvailabilityInFreeAndServicePresentations()
    {
        string code = ReadProjectFile("UI", "MainWindow.xaml.cs");
        string free = Segment(code, "private void ApplyFreePresentation", "private void ApplyServicePresentation");
        string service = Segment(code, "private void ApplyServicePresentation", "private void ShowServiceConnectionStatus");

        Assert.Contains("UpdateInstructionAvailability();", free);
        Assert.Contains("UpdateInstructionAvailability();", service);
        Assert.Contains("DetailedManualFixButton.Visibility = _lastDiagnostics?.Issues is { Count: > 0 }", code);
    }

    [Fact]
    public void MainWindow_UsesCurrentSnapshotWithoutGuardOrNewDiagnostics()
    {
        string code = ReadProjectFile("UI", "MainWindow.xaml.cs");
        string handler = Segment(code, "private void DetailedManualFix_Click", "private async void SimpleFix_Click");

        Assert.Contains("_lastDiagnostics?.Issues", handler);
        Assert.Contains("new InstructionWindow(issues)", handler);
        Assert.DoesNotContain("CreateLicenseGuard", handler);
        Assert.DoesNotContain("RefreshTopologyAsync", handler);
        Assert.DoesNotContain("DeveloperDiagnosticsWindow", handler);
    }

    [Fact]
    public void InstructionWindow_RendersOnlyDiagnosticIssueTitles()
    {
        string xaml = ReadProjectFile("UI", "InstructionWindow.xaml");
        string code = ReadProjectFile("UI", "InstructionWindow.xaml.cs");

        Assert.Contains("Content=\"Инструкция\"", ReadProjectFile("UI", "MainWindow.xaml"));
        Assert.Contains("issue.Title", code);
        Assert.DoesNotContain("TechnicalDetails", code);
        Assert.DoesNotContain("Evidence", code);
        Assert.DoesNotContain("SuggestedFix", code);
        Assert.DoesNotContain("UserMessage", code);
        Assert.DoesNotContain("Code", code);
        Assert.DoesNotContain("StaticResource", xaml);
        Assert.Contains("Закрыть", xaml);
    }

    [Fact]
    public void InstructionAvailability_RefreshesIndependentlyOfPaidExecution()
    {
        string code = ReadProjectFile("UI", "MainWindow.xaml.cs");
        string refresh = Segment(code, "private void ApplyPointStatusRefresh", "private void SetTopologyChecking");

        Assert.Contains("UpdateInstructionAvailability();", refresh);
        Assert.Contains("bool canAutoFix = _paidExecutionEnabled", refresh);
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
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            path = Directory.GetParent(path)?.FullName ?? path;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
