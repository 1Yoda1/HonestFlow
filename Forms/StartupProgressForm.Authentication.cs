using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using HonestFlow.Application.Auth;

namespace HonestFlow
{
    public partial class StartupProgressForm
    {
        private readonly Panel _authenticationPanel = new();
        private readonly TextBox _passwordBox = new();
        private readonly Button _loginButton = new();
        private readonly Button _diagnosticsButton = new();
        private readonly Label _authenticationMessage = new();
        private TaskCompletionSource<string> _passwordRequest;

        public Task<string> RequestSellerPasswordAsync(string errorMessage = null)
        {
            _passwordRequest = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            ClientSize = new Size(460, 342);
            _authenticationPanel.Visible = true;
            SetAuthenticationControlsEnabled(true);
            _passwordBox.Clear();
            _authenticationMessage.Text = string.IsNullOrWhiteSpace(errorMessage)
                ? "Введите пароль продавца для определения точки и проверки лицензии."
                : errorMessage;
            _authenticationMessage.ForeColor = string.IsNullOrWhiteSpace(errorMessage)
                ? Color.FromArgb(51, 65, 85)
                : Color.FromArgb(220, 38, 38);
            SetProgress(100, "Конфигурация получена. Выполните вход.");
            _passwordBox.Focus();
            return _passwordRequest.Task;
        }

        public void ReportLicenseAuthentication(LicenseAuthenticationProgress progress)
        {
            if (progress == null || IsDisposed)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => ReportLicenseAuthentication(progress)));
                return;
            }

            SetAuthenticationControlsEnabled(false);
            _progressBar.Style = ProgressBarStyle.Marquee;
            _authenticationMessage.ForeColor = Color.FromArgb(37, 99, 235);
            _authenticationMessage.Text = progress.Stage switch
            {
                LicenseAuthenticationStage.CheckingPassword => "Проверяем пароль продавца...",
                LicenseAuthenticationStage.ClientResolved => $"Точка определена: {progress.ClientName}",
                LicenseAuthenticationStage.CheckingDeviceAndLicense => "Проверяем устройство и лицензию...",
                LicenseAuthenticationStage.Completed => "Проверка лицензии завершена.",
                _ => "Выполняется проверка..."
            };
        }

        public void ShowAuthenticationError(string message)
        {
            _progressBar.Style = ProgressBarStyle.Blocks;
            _progressBar.Value = 100;
            _authenticationMessage.Text = string.IsNullOrWhiteSpace(message)
                ? "Не удалось выполнить вход. Проверьте пароль."
                : message;
            _authenticationMessage.ForeColor = Color.FromArgb(220, 38, 38);
        }

        public void ShowAuthenticationSuccess(string clientName)
        {
            _progressBar.Style = ProgressBarStyle.Blocks;
            _progressBar.Value = 100;
            _authenticationMessage.Text =
                $"Вход выполнен: {clientName}. Открываем HonestFlow...";
            _authenticationMessage.ForeColor = Color.FromArgb(22, 163, 74);
        }

        private void ConfigureAuthenticationPanel()
        {
            _authenticationPanel.SetBounds(22, 154, 416, 170);
            _authenticationPanel.BackColor = Color.White;
            _authenticationPanel.BorderStyle = BorderStyle.FixedSingle;
            _authenticationPanel.Visible = false;

            var passwordLabel = new Label
            {
                Left = 16,
                Top = 12,
                Width = 180,
                Height = 22,
                Text = "Пароль продавца",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };
            _passwordBox.SetBounds(16, 38, 382, 27);
            _passwordBox.UseSystemPasswordChar = true;

            _authenticationMessage.SetBounds(16, 72, 382, 38);
            _authenticationMessage.AutoEllipsis = true;

            _diagnosticsButton.SetBounds(16, 119, 174, 34);
            _diagnosticsButton.Text = "Только диагностика";
            _diagnosticsButton.FlatStyle = FlatStyle.Flat;
            _diagnosticsButton.FlatAppearance.BorderColor = Color.FromArgb(180, 190, 205);
            _diagnosticsButton.Click += (_, _) => CompletePasswordRequest(null);

            _loginButton.SetBounds(198, 119, 200, 34);
            _loginButton.Text = "Войти и проверить лицензию";
            _loginButton.BackColor = Color.FromArgb(37, 99, 235);
            _loginButton.ForeColor = Color.White;
            _loginButton.FlatStyle = FlatStyle.Flat;
            _loginButton.FlatAppearance.BorderSize = 0;
            _loginButton.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(_passwordBox.Text))
                {
                    ShowAuthenticationError("Введите пароль продавца.");
                    _passwordBox.Focus();
                    return;
                }

                CompletePasswordRequest(_passwordBox.Text);
            };

            _passwordBox.KeyDown += (_, args) =>
            {
                if (args.KeyCode != Keys.Enter)
                    return;

                args.SuppressKeyPress = true;
                _loginButton.PerformClick();
            };

            _authenticationPanel.Controls.AddRange(new Control[]
            {
                passwordLabel,
                _passwordBox,
                _authenticationMessage,
                _diagnosticsButton,
                _loginButton
            });
            Controls.Add(_authenticationPanel);
        }

        private void SetAuthenticationControlsEnabled(bool enabled)
        {
            _passwordBox.Enabled = enabled;
            _loginButton.Enabled = enabled;
            _diagnosticsButton.Enabled = enabled;
        }

        private void CompletePasswordRequest(string password)
        {
            _passwordRequest?.TrySetResult(password);
        }
    }
}
