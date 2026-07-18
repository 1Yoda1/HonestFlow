using System.Drawing;
using System.Windows.Forms;

namespace HonestFlow
{
    public sealed class SellerLoginForm : Form
    {
        private readonly TextBox _password = new TextBox
        {
            UseSystemPasswordChar = true
        };

        public SellerLoginForm()
        {
            Text = "Вход продавца";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(460, 235);

            var title = new Label
            {
                Left = 22,
                Top = 18,
                Width = 415,
                Height = 32,
                Text = "Вход в HonestFlow",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 31, 51)
            };
            var explanation = new Label
            {
                Left = 22,
                Top = 53,
                Width = 415,
                Height = 42,
                Text = "Пароль продавца определяет клиента и запускает проверку лицензии."
            };
            var passwordLabel = new Label
            {
                Left = 22,
                Top = 105,
                Width = 180,
                Height = 22,
                Text = "Пароль продавца"
            };
            _password.SetBounds(22, 130, 415, 27);

            var login = new Button
            {
                Left = 237,
                Top = 180,
                Width = 120,
                Height = 34,
                Text = "Проверить доступ",
                DialogResult = DialogResult.OK
            };
            var diagnostics = new Button
            {
                Left = 92,
                Top = 180,
                Width = 140,
                Height = 34,
                Text = "Только диагностика",
                DialogResult = DialogResult.Cancel
            };

            login.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(_password.Text))
                {
                    DialogResult = DialogResult.None;
                    MessageBox.Show(
                        this,
                        "Введите пароль продавца.",
                        "Вход продавца",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    _password.Focus();
                }
            };

            Controls.AddRange(new Control[]
            {
                title, explanation, passwordLabel, _password, diagnostics, login
            });
            AcceptButton = login;
            CancelButton = diagnostics;
            Shown += (_, _) => _password.Focus();
        }

        public string Password => _password.Text;
    }
}
