using HonestFlow.Application.Installation;
using HonestFlow.Application.Lm;
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
    }
}
