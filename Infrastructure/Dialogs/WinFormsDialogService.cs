using System.Windows.Forms;

namespace HonestFlow.Infrastructure.Dialogs
{
    public class WinFormsDialogService : IUserDialogService
    {
        private readonly IWin32Window _owner;

        public WinFormsDialogService(IWin32Window owner = null)
        {
            _owner = owner;
        }

        public void ShowInformation(string message, string title)
        {
            Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void ShowWarning(string message, string title)
        {
            Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public void ShowError(string message, string title)
        {
            Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public bool Confirm(string message, string title, UserDialogIcon icon = UserDialogIcon.Warning)
        {
            var result = Show(message, title, MessageBoxButtons.YesNo, ToMessageBoxIcon(icon));
            return result == DialogResult.Yes;
        }

        private DialogResult Show(string message, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return _owner == null
                ? MessageBox.Show(message, title, buttons, icon)
                : MessageBox.Show(_owner, message, title, buttons, icon);
        }

        private static MessageBoxIcon ToMessageBoxIcon(UserDialogIcon icon)
        {
            return icon switch
            {
                UserDialogIcon.Error => MessageBoxIcon.Error,
                UserDialogIcon.Warning => MessageBoxIcon.Warning,
                _ => MessageBoxIcon.Information
            };
        }
    }
}
