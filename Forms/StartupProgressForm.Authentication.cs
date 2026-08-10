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
        private readonly Label _loginLabel = new();
        private readonly TextBox _loginBox = new();
        private readonly Label _passwordLabel = new();
        private readonly TextBox _passwordBox = new();
        private readonly Button _loginButton = new();
        private readonly Button _diagnosticsButton = new();
        private readonly Label _authenticationMessage = new();
        private TaskCompletionSource<SellerCredentials> _passwordRequest;
        private TaskCompletionSource<bool> _rememberedLoginRequest;
        private bool _confirmingRememberedLogin;

        public Task<SellerCredentials> RequestSellerPasswordAsync(string errorMessage = null)
        {
            _confirmingRememberedLogin = false;
            _passwordRequest = new TaskCompletionSource<SellerCredentials>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            ClientSize = new Size(460, 342);
            _authenticationPanel.Visible = true;
            _loginLabel.Visible = true;
            _loginBox.Visible = true;
            _passwordLabel.Visible = true;
            _passwordBox.Visible = true;
            _diagnosticsButton.Text = "Только диагностика";
            _loginButton.Text = "Войти и проверить лицензию";
            SetAuthenticationControlsEnabled(true);
            _passwordBox.Clear();
            _loginBox.Clear();
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

        public Task<bool> RequestRememberedSellerConfirmationAsync(string clientName)
        {
            _confirmingRememberedLogin = true;
            _rememberedLoginRequest = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            ClientSize = new Size(460, 342);
            _authenticationPanel.Visible = true;
            _passwordLabel.Visible = false;
            _passwordBox.Visible = false;
            _loginLabel.Visible = false;
            _loginBox.Visible = false;
            _authenticationMessage.Text =
                $"Сохранённый вход для точки «{clientName}» актуален.";
            _authenticationMessage.ForeColor = Color.FromArgb(22, 163, 74);
            _diagnosticsButton.Text = "Ввести другой пароль";
            _loginButton.Text = "Продолжить";
            SetProgress(100, "Подтвердите сохранённый вход.");
            return _rememberedLoginRequest.Task;
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

        public void ShowPreparationStatus(string message, bool isError)
        {
            _authenticationMessage.Text = message;
            _authenticationMessage.ForeColor = isError
                ? Color.FromArgb(220, 38, 38)
                : Color.FromArgb(37, 99, 235);
        }

        private void ConfigureAuthenticationPanel()
        {
            _authenticationPanel.SetBounds(22, 154, 416, 170);
            _authenticationPanel.BackColor = Color.White;
            _authenticationPanel.BorderStyle = BorderStyle.FixedSingle;
            _authenticationPanel.Visible = false;

            _loginLabel.SetBounds(16, 8, 180, 20);
            _loginLabel.Text = "Логин";
            _loginBox.SetBounds(16, 28, 382, 25);
            _passwordLabel.SetBounds(16, 57, 180, 20);
            _passwordLabel.Text = "Пароль продавца";
            _passwordLabel.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            _passwordBox.SetBounds(16, 77, 382, 25);
            _passwordBox.UseSystemPasswordChar = true;

            _authenticationMessage.SetBounds(16, 106, 382, 30);
            _authenticationMessage.AutoEllipsis = true;

            _diagnosticsButton.SetBounds(16, 136, 174, 28);
            _diagnosticsButton.Text = "Только диагностика";
            _diagnosticsButton.FlatStyle = FlatStyle.Flat;
            _diagnosticsButton.FlatAppearance.BorderColor = Color.FromArgb(180, 190, 205);
            _diagnosticsButton.Click += (_, _) =>
            {
                if (_confirmingRememberedLogin)
                    _rememberedLoginRequest?.TrySetResult(false);
                else
                    CompletePasswordRequest(null);
            };

            _loginButton.SetBounds(198, 136, 200, 28);
            _loginButton.Text = "Войти и проверить лицензию";
            _loginButton.BackColor = Color.FromArgb(37, 99, 235);
            _loginButton.ForeColor = Color.White;
            _loginButton.FlatStyle = FlatStyle.Flat;
            _loginButton.FlatAppearance.BorderSize = 0;
            _loginButton.Click += (_, _) =>
            {
                if (_confirmingRememberedLogin)
                {
                    _rememberedLoginRequest?.TrySetResult(true);
                    return;
                }

                if (string.IsNullOrWhiteSpace(_loginBox.Text) || string.IsNullOrWhiteSpace(_passwordBox.Text))
                {
                    ShowAuthenticationError("Введите пароль продавца.");
                    (string.IsNullOrWhiteSpace(_loginBox.Text) ? _loginBox : _passwordBox).Focus();
                    return;
                }

                CompletePasswordRequest(new SellerCredentials(_loginBox.Text.Trim(), _passwordBox.Text));
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
                _loginLabel,
                _loginBox,
                _passwordLabel,
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
            _loginBox.Enabled = enabled;
            _loginButton.Enabled = enabled;
            _diagnosticsButton.Enabled = enabled;
        }

        private void CompletePasswordRequest(SellerCredentials credentials)
        {
            _passwordRequest?.TrySetResult(credentials);
        }
    }
}
