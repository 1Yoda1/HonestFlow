namespace HonestFlow.Models
{
    public class SupportMailSettings
    {
        public string SmtpHost { get; set; } = "smtp.yandex.ru";
        public int SmtpPort { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public string SenderEmail { get; set; }
        public string SenderPassword { get; set; }
        public string SenderDisplayName { get; set; } = "HonestFlow Help";
        public string RecipientEmail { get; set; }
    }
}
