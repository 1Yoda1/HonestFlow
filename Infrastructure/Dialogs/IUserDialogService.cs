namespace HonestFlow.Infrastructure.Dialogs
{
    public enum UserDialogIcon
    {
        Information,
        Warning,
        Error
    }

    public interface IUserDialogService
    {
        void ShowInformation(string message, string title);
        void ShowWarning(string message, string title);
        void ShowError(string message, string title);
        bool Confirm(string message, string title, UserDialogIcon icon = UserDialogIcon.Warning);
    }
}
