using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace HonestFlow.Tests;

public sealed class MainWindowTopologyStatusDotContractTests
{
    [Fact]
    public void DetailedTopology_StatusDotsStartNeutralAndAreNamed()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(ProjectFile("HonestFlow.WpfPrototype", "MainWindow.xaml"));

        string[] expectedNames =
        {
            "GismtNodeStatusDot", "EsmNodeStatusDot", "ControllerNodeStatusDot", "LmNodeStatusDot", "KktNodeStatusDot"
        };

        XElement[] dots = document.Descendants(presentation + "Ellipse")
            .Where(element => expectedNames.Contains((string)element.Attribute(xaml + "Name")))
            .ToArray();

        Assert.Equal(expectedNames.Length, dots.Length);
        Assert.All(dots, dot => Assert.Equal("#94A3B8", (string)dot.Attribute("Fill")));
    }

    [Fact]
    public void DetailedTopology_StatusDotsFollowComponentPresentationState()
    {
        string code = File.ReadAllText(ProjectFile("HonestFlow.WpfPrototype", "MainWindow.xaml.cs"));

        Assert.Equal(2, code.Split("statusDot.Fill = color;").Length - 1);
        Assert.Contains("TopologyVisualState.Healthy => BrushFrom(\"#0E9F6E\")", code);
        Assert.Contains("TopologyVisualState.Missing => BrushFrom(\"#D91532\")", code);
        Assert.Contains("TopologyVisualState.Ignored => BrushFrom(\"#94A3B8\")", code);
        Assert.Contains("_ => BrushFrom(\"#F4B740\")", code);

        foreach (string dotName in new[]
                 {
                     "GismtNodeStatusDot", "EsmNodeStatusDot", "ControllerNodeStatusDot", "LmNodeStatusDot", "KktNodeStatusDot"
                 })
        {
            Assert.Contains(dotName, code);
        }
    }

    private static string ProjectFile(params string[] parts) => Path.GetFullPath(Path.Combine(
        new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(parts).ToArray()));
}
