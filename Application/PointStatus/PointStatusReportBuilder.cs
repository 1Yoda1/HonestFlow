using System;
using System.Text;

namespace HonestFlow.Application.PointStatus
{
    public sealed class PointStatusReportBuilder
    {
        public string Build(PointStatusResult result, DateTimeOffset? generatedAt = null)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            DateTimeOffset timestamp = generatedAt ?? DateTimeOffset.Now;
            var report = new StringBuilder();
            report.AppendLine("ОТЛАДОЧНЫЙ СНИМОК СОСТОЯНИЯ ТОЧКИ");
            report.AppendLine($"Сформирован: {timestamp:dd.MM.yyyy HH:mm:ss}");
            report.AppendLine("Чувствительные идентификаторы ККТ и токены намеренно не выводятся.");

            AppendNode(report, "ЛМ ЧЗ", result.Lm);
            AppendNode(report, "Контроллер", result.Controller);
            AppendNode(report, "ЕСМ", result.Esm);
            AppendNode(report, "ККТ", result.Kkt);
            AppendNode(report, "Облако", result.Cloud);
            AppendNode(report, "RuDesktop", result.RuDesktop);
            return report.ToString();
        }

        private static void AppendNode(StringBuilder report, string name, NodeStatus status)
        {
            report.AppendLine();
            report.AppendLine(new string('=', 72));
            report.AppendLine(name);
            report.AppendLine(new string('-', 72));
            if (status == null)
            {
                report.AppendLine("Данные отсутствуют.");
                return;
            }

            report.AppendLine($"Уровень: {status.Level}");
            report.AppendLine($"Короткий статус: {status.ShortText}");
            report.AppendLine($"Текст в интерфейсе: {ValueOrDash(status.StatusText)}");
            report.AppendLine($"Доступное действие: {status.ActionText}");
            report.AppendLine("Службы:");
            if (status.Services.Count == 0)
            {
                report.AppendLine("  — источник не содержит Windows-служб");
            }
            else
            {
                foreach (ServiceSnapshot service in status.Services)
                    report.AppendLine($"  — {service.ServiceName}: {service.State}");
            }

            report.AppendLine("Исходные данные и расчёт:");
            report.AppendLine(string.IsNullOrWhiteSpace(status.Details)
                ? "  — подробности отсутствуют"
                : status.Details);
        }

        private static string ValueOrDash(string value) =>
            string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
    }
}
