namespace HonestFlow.Infrastructure.Dialogs
{
    public enum UserNotificationSeverity
    {
        Information,
        Success,
        Warning,
        Error
    }

    public interface IUserNotificationSink
    {
        void ShowNotification(
            string message,
            string title,
            UserNotificationSeverity severity);
    }
}
