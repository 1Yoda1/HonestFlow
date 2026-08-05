using System;
using HonestFlow.Application.PointStatus;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class PointStatusReportBuilderTests
    {
        [Fact]
        public void Build_FormatsNodesServicesAndStableTimestamp()
        {
            var result = new PointStatusResult
            {
                Lm = new NodeStatus(
                    NodeLevel.Warning,
                    "Требуется внимание",
                    "Диагностические подробности",
                    new[] { new ServiceSnapshot("HonestFlow.Test", "Running") },
                    "Проверить состояние"),
                Controller = null
            };
            var timestamp = new DateTimeOffset(2026, 8, 6, 12, 34, 56, TimeSpan.Zero);

            string report = new PointStatusReportBuilder().Build(result, timestamp);

            Assert.Contains("Сформирован: 06.08.2026 12:34:56", report);
            Assert.Contains("ЛМ ЧЗ", report);
            Assert.Contains("HonestFlow.Test: Running", report);
            Assert.Contains("Диагностические подробности", report);
            Assert.Contains("Контроллер", report);
            Assert.Contains("Данные отсутствуют.", report);
        }

        [Fact]
        public void Build_DoesNotIncludeSensitiveKktIdentifiers()
        {
            var result = new PointStatusResult
            {
                Kkt = new NodeStatus(NodeLevel.Ok, "Работает", "Безопасные сведения")
            };

            string report = new PointStatusReportBuilder().Build(result);

            Assert.Contains("Чувствительные идентификаторы ККТ и токены намеренно не выводятся.", report);
            Assert.Contains("Безопасные сведения", report);
        }
    }
}
