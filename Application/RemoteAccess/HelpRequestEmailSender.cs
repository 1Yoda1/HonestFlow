using System;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using HonestFlow.Application.Core;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Models;
using Newtonsoft.Json;

namespace HonestFlow.Application.RemoteAccess
{
    public class HelpRequestEmailSender
    {
        private readonly ILogService _log;

        public HelpRequestEmailSender(ILogService log)
        {
            _log = log;
        }

        public async Task Send(HelpRequestData request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            SupportMailSettings settings = ConfigManager.LoadSupportMailSettings();
            ValidateSettings(settings);

            using var message = new MailMessage();
            message.From = new MailAddress(settings.SenderEmail, ValueOrDash(settings.SenderDisplayName));
            message.To.Add(settings.RecipientEmail);
            message.Subject = BuildSubject(request);
            message.SubjectEncoding = Encoding.UTF8;
            message.BodyEncoding = Encoding.UTF8;
            message.IsBodyHtml = false;
            message.Body = BuildBody(request);

            using var smtp = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
            {
                EnableSsl = settings.EnableSsl,
                Credentials = new NetworkCredential(settings.SenderEmail, settings.SenderPassword),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 30000
            };

            await smtp.SendMailAsync(message);
            _log?.LogUser($"Запрос помощи отправлен: {ValueOrDash(request.ClientName)}, {ValueOrDash(request.InnMasked)}, проблема: {ValueOrDash(request.ProblemType)}");
        }

        private static void ValidateSettings(SupportMailSettings settings)
        {
            if (settings == null)
                throw new InvalidOperationException("Не найдены настройки почты поддержки support_mail_encrypted.json.");

            if (string.IsNullOrWhiteSpace(settings.SmtpHost))
                throw new InvalidOperationException("В настройках почты поддержки не указан SmtpHost.");

            if (settings.SmtpPort <= 0)
                throw new InvalidOperationException("В настройках почты поддержки указан некорректный SmtpPort.");

            if (string.IsNullOrWhiteSpace(settings.SenderEmail))
                throw new InvalidOperationException("В настройках почты поддержки не указан SenderEmail.");

            if (string.IsNullOrWhiteSpace(settings.SenderPassword))
                throw new InvalidOperationException("В настройках почты поддержки не указан SenderPassword.");

            if (string.IsNullOrWhiteSpace(settings.RecipientEmail))
                throw new InvalidOperationException("В настройках почты поддержки не указан RecipientEmail.");
        }

        private static string BuildSubject(HelpRequestData request)
        {
            return $"HF_HELP | {SubjectPart(request.ClientName)} | {SubjectPart(request.InnMasked)} | {SubjectPart(request.MachineName)} | {SubjectPart(request.RequestId)}";
        }

        private static string BuildBody(HelpRequestData request)
        {
            string json = JsonConvert.SerializeObject(request, Formatting.Indented);

            return
                $"RequestId: {ValueOrDash(request.RequestId)}" + Environment.NewLine +
                $"ClientName: {ValueOrDash(request.ClientName)}" + Environment.NewLine +
                $"InnMasked: {ValueOrDash(request.InnMasked)}" + Environment.NewLine +
                $"MachineName: {ValueOrDash(request.MachineName)}" + Environment.NewLine +
                $"WindowsUser: {ValueOrDash(request.WindowsUser)}" + Environment.NewLine +
                $"RuDesktopId: {ValueOrDash(request.RuDesktopId)}" + Environment.NewLine +
                $"HonestFlowVersion: {ValueOrDash(request.HonestFlowVersion)}" + Environment.NewLine +
                $"OS: {ValueOrDash(request.OsVersion)}" + Environment.NewLine +
                $"Architecture: {ValueOrDash(request.Architecture)}" + Environment.NewLine +
                $"Administrator: {request.IsAdministrator}" + Environment.NewLine +
                $"ClientId: {ValueOrDash(request.ClientId)}" + Environment.NewLine +
                $"DeviceId: {ValueOrDash(request.DeviceId)}" + Environment.NewLine +
                $"License: {ValueOrDash(request.LicenseDecision)}" + Environment.NewLine +
                $"LicenseTechnicalCode: {ValueOrDash(request.LicenseTechnicalCode)}" + Environment.NewLine +
                $"LicenseSource: {ValueOrDash(request.LicenseSource)}" + Environment.NewLine +
                $"LicenseRevision: {request.LicenseRevision?.ToString() ?? "-"}" + Environment.NewLine +
                $"FiscalAddress: {ValueOrDash(request.FiscalAddress)}" + Environment.NewLine +
                $"ProblemType: {ValueOrDash(request.ProblemType)}" + Environment.NewLine +
                $"CreatedAt: {ValueOrDash(request.CreatedAt)}" + Environment.NewLine +
                Environment.NewLine +
                "PointStatus:" + Environment.NewLine +
                FormatPointStatus(request.PointStatus) + Environment.NewLine +
                Environment.NewLine +
                "Message:" + Environment.NewLine +
                ValueOrDash(request.Message) + Environment.NewLine +
                Environment.NewLine +
                "```json" + Environment.NewLine +
                json + Environment.NewLine +
                "```";
        }

        private static string FormatPointStatus(HelpRequestPointStatus status)
        {
            if (status == null)
                return "-";

            var result = new StringBuilder();
            result.AppendLine($"CheckedAt: {ValueOrDash(status.CheckedAt)}");
            if (!string.IsNullOrWhiteSpace(status.Error))
                result.AppendLine($"Error: {status.Error}");

            AppendNode(result, "LM", status.Lm);
            AppendNode(result, "Controller", status.Controller);
            AppendNode(result, "ESM", status.Esm);
            AppendNode(result, "KKT", status.Kkt);
            AppendNode(result, "Cloud", status.Cloud);
            AppendNode(result, "RuDesktop", status.RuDesktop);
            return result.ToString().TrimEnd();
        }

        private static void AppendNode(StringBuilder result, string name, HelpRequestNodeStatus node)
        {
            if (node == null)
            {
                result.AppendLine($"{name}: -");
                return;
            }

            result.AppendLine($"{name}: [{ValueOrDash(node.Level)}] {ValueOrDash(node.ShortText)}");
            if (!string.IsNullOrWhiteSpace(node.StatusText))
                result.AppendLine($"  Status: {node.StatusText}");
            if (!string.IsNullOrWhiteSpace(node.Details))
                result.AppendLine($"  Details: {node.Details.Replace(Environment.NewLine, " | ")}");
            foreach (HelpRequestServiceStatus service in node.Services ?? Array.Empty<HelpRequestServiceStatus>())
                result.AppendLine($"  Service {ValueOrDash(service.Name)}: {ValueOrDash(service.State)}");
        }

        private static string SubjectPart(string value)
        {
            value = ValueOrDash(value);
            return value.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static string ValueOrDash(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }
    }

    public sealed class HelpRequestData
    {
        public string RequestId { get; set; }
        public string ClientName { get; set; }
        public string InnMasked { get; set; }
        public string MachineName { get; set; }
        public string WindowsUser { get; set; }
        public string RuDesktopId { get; set; }
        public string HonestFlowVersion { get; set; }
        public string OsVersion { get; set; }
        public string Architecture { get; set; }
        public bool IsAdministrator { get; set; }
        public string ClientId { get; set; }
        public string DeviceId { get; set; }
        public string LicenseDecision { get; set; }
        public string LicenseTechnicalCode { get; set; }
        public string LicenseSource { get; set; }
        public long? LicenseRevision { get; set; }
        public string FiscalAddress { get; set; }
        public string ProblemType { get; set; }
        public string Message { get; set; }
        public string CreatedAt { get; set; }
        public HelpRequestPointStatus PointStatus { get; set; }
    }

    public sealed class HelpRequestPointStatus
    {
        public string CheckedAt { get; set; }
        public string Error { get; set; }
        public HelpRequestNodeStatus Lm { get; set; }
        public HelpRequestNodeStatus Controller { get; set; }
        public HelpRequestNodeStatus Esm { get; set; }
        public HelpRequestNodeStatus Kkt { get; set; }
        public HelpRequestNodeStatus Cloud { get; set; }
        public HelpRequestNodeStatus RuDesktop { get; set; }
    }

    public sealed class HelpRequestNodeStatus
    {
        public string Level { get; set; }
        public string ShortText { get; set; }
        public string StatusText { get; set; }
        public string Details { get; set; }
        public HelpRequestServiceStatus[] Services { get; set; } = Array.Empty<HelpRequestServiceStatus>();
    }

    public sealed class HelpRequestServiceStatus
    {
        public string Name { get; set; }
        public string State { get; set; }
    }
}
