using HonestFlow.Application.Core;
using HonestFlow.Application.RemoteAccess;
using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace HonestFlow.Application.Diagnostics
{
    public sealed class DiagnosticsEmailSender
    {
        private const string SmtpHost = "smtp.yandex.ru";
        private const int SmtpPort = 587;
        private const string SenderEmail = "sdsk@morkovka.tech";
        private const string SenderPassword = "kppzdcwhpmwvryoh";
        private const string RecipientEmail = "spi@morkovka.tech";
        public const int SendAttempts = 3;

        private readonly ILogService _log;
        private readonly RuDesktopService _ruDesktopService;

        public DiagnosticsEmailSender(ILogService log, RuDesktopService ruDesktopService)
        {
            _log = log;
            _ruDesktopService = ruDesktopService;
        }

        public async Task SendWithRetries(string archivePath, Action<int, string> reportProgress, string fiscalAddress = null)
        {
            Exception lastError = null;

            for (int attempt = 1; attempt <= SendAttempts; attempt++)
            {
                try
                {
                    int progress = 35 + attempt * 15;
                    reportProgress?.Invoke(Math.Min(progress, 95), $"Попытка отправки {attempt}/{SendAttempts}...");

                    await Send(archivePath, fiscalAddress);
                    reportProgress?.Invoke(95, "Архив отправлен на почту");
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    _log?.LogDebug($"Ошибка отправки диагностики, попытка {attempt}/{SendAttempts}: {ex.Message}");

                    if (attempt < SendAttempts)
                        await Task.Delay(1500);
                }
            }

            throw new InvalidOperationException($"Архив собран, но не отправлен после {SendAttempts} попыток: {lastError?.Message}", lastError);
        }

        private async Task Send(string archivePath, string fiscalAddress)
        {
            using var message = new MailMessage();
            message.From = new MailAddress(SenderEmail, "HonestFlow Diagnostics");
            message.To.Add(RecipientEmail);
            message.Subject = $"HonestFlow диагностика {Environment.MachineName} {DateTime.Now:yyyy-MM-dd HH:mm}";
            string ruDesktopId = await _ruDesktopService.GetId() ?? "не найден";
            message.Body =
                "Диагностический архив HonestFlow во вложении." + Environment.NewLine +
                $"ПК: {Environment.MachineName}" + Environment.NewLine +
                $"Пользователь: {Environment.UserName}" + Environment.NewLine +
                $"Адрес: {FormatAddress(fiscalAddress)}" + Environment.NewLine +
                $"RuDesktop ID: {ruDesktopId}" + Environment.NewLine +
                $"Время: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            message.Attachments.Add(new Attachment(archivePath));

            using var client = new SmtpClient(SmtpHost, SmtpPort)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(SenderEmail, SenderPassword),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 30000
            };

            await client.SendMailAsync(message);
        }

        private static string FormatAddress(string fiscalAddress)
        {
            return string.IsNullOrWhiteSpace(fiscalAddress)
                ? "не найден в логе ККТ"
                : fiscalAddress.Trim();
        }
    }
}
