using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Models;

namespace HonestFlow.Infrastructure.Licensing
{
    public sealed class SmtpDeviceRegistrationRequestSender : IDeviceRegistrationRequestSender
    {
        public const string SubjectMarker = "[HONESTFLOW-DEVICE-REGISTRATION]";

        public async Task SendAsync(string requestJson, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(requestJson))
                throw new ArgumentException("Registration request is required.", nameof(requestJson));

            SupportMailSettings settings = ConfigManager.LoadSupportMailSettings();
            Validate(settings);

            byte[] attachmentBytes = new UTF8Encoding(false).GetBytes(requestJson);
            using var attachmentStream = new MemoryStream(attachmentBytes, writable: false);
            using var attachment = new Attachment(
                attachmentStream,
                "honestflow-device-registration.json",
                "application/json");
            using var message = new MailMessage
            {
                From = new MailAddress(settings.SenderEmail, "HonestFlow Registration"),
                Subject = SubjectMarker,
                Body = "Автоматическая заявка регистрации устройства HonestFlow находится во вложении."
            };
            message.To.Add(settings.SenderEmail);
            message.Attachments.Add(attachment);

            using var smtp = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
            {
                EnableSsl = settings.EnableSsl,
                Credentials = new NetworkCredential(settings.SenderEmail, settings.SenderPassword),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 30000
            };
            await smtp.SendMailAsync(message);
        }

        private static void Validate(SupportMailSettings settings)
        {
            if (settings == null ||
                string.IsNullOrWhiteSpace(settings.SmtpHost) ||
                settings.SmtpPort <= 0 ||
                string.IsNullOrWhiteSpace(settings.SenderEmail) ||
                string.IsNullOrWhiteSpace(settings.SenderPassword))
            {
                throw new InvalidOperationException("Настройки почты для регистрации устройств не заполнены.");
            }
        }
    }
}
