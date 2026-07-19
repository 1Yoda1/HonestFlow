using HonestFlow.Application.Core;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Models;
using System;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace HonestFlow.Application.Feedback
{
    public sealed class AppRatingEmailSender
    {
        private readonly ILogService _log;

        public AppRatingEmailSender(ILogService log)
        {
            _log = log;
        }

        public async Task Send(string clientName, string pointAddress)
        {
            SupportMailSettings settings = ConfigManager.LoadSupportMailSettings();
            ValidateSettings(settings);

            string safeClientName = ValueOrFallback(clientName, "ИП не указан");
            string safeAddress = ValueOrFallback(pointAddress, "адрес не указан");

            using var message = new MailMessage();
            message.From = new MailAddress(settings.SenderEmail, "HonestFlow");
            message.To.Add(settings.RecipientEmail);
            message.Subject = $"HonestFlow понравился | {SubjectPart(safeClientName)}";
            message.SubjectEncoding = Encoding.UTF8;
            message.BodyEncoding = Encoding.UTF8;
            message.IsBodyHtml = false;
            message.Body =
                "Ваше приложение понравилось!" + Environment.NewLine +
                $"ИП: {safeClientName}" + Environment.NewLine +
                $"Адрес точки: {safeAddress}";

            using var smtp = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
            {
                EnableSsl = settings.EnableSsl,
                Credentials = new NetworkCredential(settings.SenderEmail, settings.SenderPassword),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 30000
            };

            await smtp.SendMailAsync(message);
            _log?.LogUser($"Отправлена положительная оценка HonestFlow от {safeClientName}");
        }

        private static void ValidateSettings(SupportMailSettings settings)
        {
            if (settings == null)
                throw new InvalidOperationException("Настройки почты поддержки недоступны.");

            if (string.IsNullOrWhiteSpace(settings.SmtpHost) || settings.SmtpPort <= 0)
                throw new InvalidOperationException("Некорректно настроен SMTP почты поддержки.");

            if (string.IsNullOrWhiteSpace(settings.SenderEmail) ||
                string.IsNullOrWhiteSpace(settings.SenderPassword) ||
                string.IsNullOrWhiteSpace(settings.RecipientEmail))
            {
                throw new InvalidOperationException("Не заполнены реквизиты почты поддержки.");
            }
        }

        private static string SubjectPart(string value)
        {
            return value.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static string ValueOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
