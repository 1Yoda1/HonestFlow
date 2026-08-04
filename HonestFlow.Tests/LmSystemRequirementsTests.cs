using HonestFlow.Application.Lm;
using HonestFlow.Application.PointStatus;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class LmSystemRequirementsTests
    {
        [Fact]
        public void Evaluate_AdequateComputer_HasNoWarnings()
        {
            var resources = new LmSystemResources(8, GiB(8), (long)GiB(2));

            LmSystemRequirementsResult result = LmSystemRequirements.Evaluate(resources);

            Assert.True(result.MeetsMinimum);
            Assert.False(result.HasWarnings);
        }

        [Fact]
        public void Evaluate_InsufficientComputer_ReportsEveryFailedMinimum()
        {
            var resources = new LmSystemResources(2, GiB(2), MiB(100));

            LmSystemRequirementsResult result = LmSystemRequirements.Evaluate(resources);

            Assert.False(result.MeetsMinimum);
            Assert.Equal(3, result.MinimumWarnings.Count);
            Assert.Contains(result.MinimumWarnings, x => x.Contains("ядер"));
            Assert.Contains(result.MinimumWarnings, x => x.Contains("Оперативная память"));
            Assert.Contains(result.MinimumWarnings, x => x.Contains("системном диске"));
        }

        [Fact]
        public void Evaluate_LowButSufficientDisk_IsRecommendationOnly()
        {
            var resources = new LmSystemResources(4, GiB(4), MiB(500));

            LmSystemRequirementsResult result = LmSystemRequirements.Evaluate(resources);

            Assert.True(result.MeetsMinimum);
            Assert.True(result.HasWarnings);
            Assert.Empty(result.MinimumWarnings);
            Assert.Single(result.Recommendations);
        }

        [Fact]
        public void Evaluate_UnknownValues_DoNotProduceFalseWarnings()
        {
            var resources = new LmSystemResources(0, 0, -1);

            LmSystemRequirementsResult result = LmSystemRequirements.Evaluate(resources);

            Assert.True(result.MeetsMinimum);
            Assert.False(result.HasWarnings);
        }

        [Fact]
        public void ApplyToLmStatus_AddsWarningWithoutReplacingActionOrServices()
        {
            var services = new[] { new ServiceSnapshot("regime", "Running") };
            var status = new NodeStatus(
                NodeLevel.Ok,
                "Готово",
                "ЛМ работает",
                services,
                "API: ready",
                NodeActionKind.InitializeLm);
            var requirements = LmSystemRequirements.Evaluate(
                new LmSystemResources(2, GiB(8), (long)GiB(2)));

            NodeStatus result = PointStatusService.ApplyLmSystemRequirements(status, requirements);

            Assert.Equal(NodeLevel.Warning, result.Level);
            Assert.Contains("ПК ниже требований", result.StatusText);
            Assert.Contains("Физических ядер", result.Details);
            Assert.Same(services, result.Services);
            Assert.Equal(NodeActionKind.InitializeLm, result.ActionKind);
        }

        [Fact]
        public void ApplyToLmStatus_DoesNotHideExistingError()
        {
            var status = new NodeStatus(NodeLevel.Error, "Ошибка", "API недоступен");
            var requirements = LmSystemRequirements.Evaluate(
                new LmSystemResources(2, GiB(2), MiB(100)));

            NodeStatus result = PointStatusService.ApplyLmSystemRequirements(status, requirements);

            Assert.Equal(NodeLevel.Error, result.Level);
            Assert.Equal("Ошибка", result.ShortText);
            Assert.Contains("API недоступен", result.Details);
            Assert.Contains("Требования к ПК", result.Details);
        }

        private static ulong GiB(ulong value) => value * 1024 * 1024 * 1024;
        private static long MiB(long value) => value * 1024 * 1024;
    }
}
