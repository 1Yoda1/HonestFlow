using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class LastAuthorizedClientHintPresentationTests
    {
        private static readonly XNamespace Presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        private static readonly XNamespace Xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        [Fact]
        public void LoginPanel_ContainsCompactNeutralHistoricalHintWithoutScrollViewer()
        {
            XDocument document = LoadStartupWindow();
            XElement panel = document.Descendants(Presentation + "Border")
                .Single(Named("LastAuthorizedClientHintPanel"));

            Assert.Equal("Collapsed", (string)panel.Attribute("Visibility"));
            Assert.Equal("#F3F7FC", (string)panel.Attribute("Background"));
            Assert.Equal("#D7E3F1", (string)panel.Attribute("BorderBrush"));
            string[] scrollViewerNames = document.Descendants(Presentation + "ScrollViewer")
                .Select(element => (string)element.Attribute(Xaml + "Name"))
                .ToArray();
            Assert.DoesNotContain("LoginPanel", scrollViewerNames);
            Assert.Equal("Top", (string)document.Descendants(Presentation + "StackPanel")
                .Single(Named("LoginPanel")).Attribute("VerticalAlignment"));
            Assert.Equal("10,8", (string)panel.Attribute("Padding"));
            Assert.Null(panel.Attribute("Height"));
            Assert.All(panel.Descendants(Presentation + "TextBlock"), textBlock =>
                Assert.Equal("Wrap", (string)textBlock.Attribute("TextWrapping")));
            Assert.Equal("LastAuthorizedClientName", (string)panel.Descendants(Presentation + "TextBlock")
                .Single(Named("LastAuthorizedClientName")).Attribute(Xaml + "Name"));
            string text = string.Join(" ", panel.Descendants(Presentation + "TextBlock")
                .Select(element => (string)element.Attribute("Text")));
            Assert.Contains("Ранее использовался клиент", text, StringComparison.Ordinal);
            Assert.Contains("потребуется освобождение", text, StringComparison.Ordinal);
        }

        [Fact]
        public void LoginPanel_KeepsCompactControlsAtStandardAndMinimumWindowSizes()
        {
            XDocument document = LoadStartupWindow();
            XElement window = document.Root;
            XElement login = document.Descendants(Presentation + "StackPanel").Single(Named("LoginPanel"));
            XElement warning = document.Descendants(Presentation + "Border").Single(Named("RememberOfflineWarning"));

            Assert.Equal("980", (string)window.Attribute("Width"));
            Assert.Equal("680", (string)window.Attribute("Height"));
            Assert.Equal("900", (string)window.Attribute("MinWidth"));
            Assert.Equal("650", (string)window.Attribute("MinHeight"));
            Assert.NotNull(login.Descendants(Presentation + "PasswordBox").Single(Named("PasswordInput")));
            Assert.NotNull(login.Descendants(Presentation + "TextBlock").Single(Named("LoginError")));
            Assert.NotNull(login.Descendants(Presentation + "Button").Single(Named("LoginButton")));
            Assert.Equal("44", (string)login.Descendants(Presentation + "PasswordBox")
                .Single(Named("PasswordInput")).Attribute("Height"));
            Assert.Equal("9,8", (string)warning.Attribute("Padding"));
            Assert.Null(warning.Attribute("Height"));
        }

        [Fact]
        public void StartupWindow_UsesHintOnlyAtFreshLoginAndWritesAfterValidatedServiceContext()
        {
            string source = File.ReadAllText(SourcePath("StartupWindow.xaml.cs"));
            string builder = File.ReadAllText(ProjectPath(
                "Application", "ServiceConnection", "ServiceRuntimeContextBuilder.cs"));

            Assert.Contains("LoadLastAuthorizedClientHintAsync", source, StringComparison.Ordinal);
            Assert.Contains("LastAuthorizedClientHintPanel.Visibility", source, StringComparison.Ordinal);
            Assert.Contains("new ServiceRuntimeContextBuilder().BuildAsync(", source, StringComparison.Ordinal);
            Assert.True(
                builder.IndexOf("new ServiceRuntimeContext(", StringComparison.Ordinal) <
                builder.IndexOf("SaveLastAuthorizedClientHintAsync", StringComparison.Ordinal));
            Assert.DoesNotContain("ClearLastAuthorizedClientHint", source, StringComparison.Ordinal);
        }

        private static Func<XElement, bool> Named(string name) =>
            element => (string)element.Attribute(Xaml + "Name") == name;

        private static XDocument LoadStartupWindow() => XDocument.Load(SourcePath("StartupWindow.xaml"));

        private static string SourcePath(string fileName) => ProjectPath("UI", fileName);

        private static string ProjectPath(params string[] parts) => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
    }
}
