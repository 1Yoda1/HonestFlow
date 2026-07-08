using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using HonestFlow.Application.Core;
using HonestFlow.Models;

namespace HonestFlow.Application.RemoteAccess
{
    public class HelpRequestEmailSender
    {
        private const string SmtpHost = "smtp.yandex.ru";
        private const int SmtpPort = 587;
        private const string SenderEmail = "sdsk@morkovka.tech";
        private const string SenderPassword = "kppzdcwhpmwvryoh";
        private const string RecipientEmail = "spi@morkovka.tech";

        private readonly ILogService _log;

        public HelpRequestEmailSender(ILogService log)
        {
            _log = log;
        }

        public async Task Send(IPData client, LastAuthorizedClientState lastAuthorizedClient, string ruDesktopId)
        {
            string clientName = client?.Name ?? lastAuthorizedClient?.Name;
            string clientInn = client?.Inn ?? lastAuthorizedClient?.Inn;
            string clientSource = client != null
                ? "текущая авторизация"
                : lastAuthorizedClient != null ? $"последняя авторизация HonestFlow ({lastAuthorizedClient.AuthorizedAt:yyyy-MM-dd HH:mm:ss})" : "не определён";

            using var message = new MailMessage();
            message.From = new MailAddress(SenderEmail, "HonestFlow Help");
            message.To.Add(RecipientEmail);
            message.Subject = $"HonestFlow: запрос помощи - {clientName ?? Environment.MachineName}";
            message.Body =
                "Оператор запросил удалённую помощь из HonestFlow." + Environment.NewLine +
                $"Клиент: {ValueOrDash(clientName)}" + Environment.NewLine +
                $"Источник клиента: {clientSource}" + Environment.NewLine +
                $"ИНН: {MaskInn(clientInn)}" + Environment.NewLine +
                $"ПК: {Environment.MachineName}" + Environment.NewLine +
                $"Пользователь Windows: {Environment.UserName}" + Environment.NewLine +
                $"RuDesktop ID: {ValueOrDash(ruDesktopId)}" + Environment.NewLine +
                $"Время: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

            using var smtp = new SmtpClient(SmtpHost, SmtpPort)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(SenderEmail, SenderPassword),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 30000
            };

            await smtp.SendMailAsync(message);
            _log?.LogUser($"Запрос помощи отправлен. RuDesktop ID: {ruDesktopId}");
        }

        private static string ValueOrDash(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }

        private static string MaskInn(string inn)
        {
            if (string.IsNullOrWhiteSpace(inn) || inn.Length < 6)
                return ValueOrDash(inn);

            return inn.Substring(0, 4) + new string('*', Math.Max(0, inn.Length - 6)) + inn.Substring(inn.Length - 2);
        }
    }
}
