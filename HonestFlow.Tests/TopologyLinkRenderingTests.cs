using System;
using System.IO;
using Xunit;

namespace HonestFlow.Tests;

public sealed class TopologyLinkRenderingTests
{
    [Fact]
    public void DetailedTopology_UsesLineStylesInsteadOfQuestionMarkers()
    {
        string markup = File.ReadAllText(ProjectFile("UI", "MainWindow.xaml"));
        string code = File.ReadAllText(ProjectFile("UI", "MainWindow.xaml.cs"));

        Assert.DoesNotContain("CloudEsmMarker", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"?\"", markup, StringComparison.Ordinal);
        Assert.Contains("TopologyVisualState.Missing => new DoubleCollection { 5, 3 }", code, StringComparison.Ordinal);
        Assert.Contains("TopologyVisualState.Uncertain => new DoubleCollection { 1, 3 }", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SimpleStatus_UsesDedicatedCrossAlignment()
    {
        string code = File.ReadAllText(ProjectFile("UI", "MainWindow.xaml.cs"));

        Assert.Contains("symbol == \"×\"", code, StringComparison.Ordinal);
        Assert.Contains("new Thickness(0, -1, 0, 0)", code, StringComparison.Ordinal);
    }

    private static string ProjectFile(params string[] parts) => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        Path.Combine(parts)));
}
