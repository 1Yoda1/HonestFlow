using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class StartupWindowPresentationContractTests
    {
        private static readonly XNamespace Presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        private static readonly XNamespace Xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        [Fact]
        public void StartupWindow_UsesFiveDomainStagesWithoutNumericProgress()
        {
            XDocument document = LoadStartupWindow();
            string[] labels = document.Descendants(Presentation + "TextBlock")
                .Select(element => (string)element.Attribute("Text"))
                .Where(text => text is "Подготовка" or "Доступ" or "Устройство" or "Лицензия" or "Запуск")
                .ToArray();

            Assert.Equal(["Подготовка", "Доступ", "Устройство", "Лицензия", "Запуск"], labels);
            Assert.Empty(document.Descendants(Presentation + "ProgressBar"));
            string[] elementNames = document.Descendants()
                .Select(element => (string)element.Attribute(Xaml + "Name"))
                .ToArray();
            Assert.DoesNotContain("LoadingPercent", elementNames);
        }

        [Fact]
        public void RememberWarning_DefaultsHiddenAndTracksUncheckedState()
        {
            XDocument document = LoadStartupWindow();
            XElement checkBox = document.Descendants(Presentation + "CheckBox")
                .Single(Named("RememberLoginCheckBox"));
            XElement warning = document.Descendants(Presentation + "Border")
                .Single(Named("RememberOfflineWarning"));

            Assert.Equal("True", (string)checkBox.Attribute("IsChecked"));
            Assert.Contains(
                warning.Descendants(Presentation + "Setter"),
                setter => (string)setter.Attribute("Property") == "Visibility" &&
                          (string)setter.Attribute("Value") == "Collapsed");

            XElement trigger = warning.Descendants(Presentation + "DataTrigger").Single();
            Assert.Equal("False", (string)trigger.Attribute("Value"));
            Assert.Contains("RememberLoginCheckBox", (string)trigger.Attribute("Binding"));
            Assert.Contains(
                trigger.Descendants(Presentation + "Setter"),
                setter => (string)setter.Attribute("Property") == "Visibility" &&
                          (string)setter.Attribute("Value") == "Visible");
        }

        [Fact]
        public void RememberWarning_UsesExactNonModalOfflineExplanation()
        {
            XElement warning = LoadStartupWindow().Descendants(Presentation + "Border")
                .Single(Named("RememberOfflineWarning"));
            string text = (string)warning.Descendants(Presentation + "TextBlock")
                .Single(element => ((string)element.Attribute("Text"))?.Contains("После закрытия") == true)
                .Attribute("Text");

            Assert.Equal(
                "После закрытия HonestFlow потребуется снова ввести код клиента.\n" +
                "Автономный запуск без интернета будет недоступен.",
                text);
        }

        private static Func<XElement, bool> Named(string name) =>
            element => (string)element.Attribute(Xaml + "Name") == name;

        private static XDocument LoadStartupWindow()
        {
            string path = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "UI",
                "StartupWindow.xaml"));
            return XDocument.Load(path);
        }
    }
}
