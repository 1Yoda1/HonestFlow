using HonestFlow.Application.Bootstrap;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Configuration;
using HonestFlow.Models;
using HonestFlow.Services.Auth;
using HonestFlow.Services.Core;
using HonestFlow.Services.Installation;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace HonestFlow
{
    public partial class MainForm : Form
    {
        private readonly ILogService _logService;
        private readonly IProgressService _progressService;
        private IAuthService _authService;
        private IInstallationService _installationService;
        private bool _useGitHubMode = false;
        private List<IPData> _gitHubIps;
        private VersionsData _gitHubVersions;

        private Form _logForm;

        public MainForm()
        {
            InitializeComponent();

            _logService = new LogService();
            _progressService = new ProgressService(progressBar, lblStatus);

            var startup = new ApplicationStartupService(_logService, _progressService).Start();
            _useGitHubMode = startup.UseGitHubMode;
            _gitHubIps = startup.GitHubIps;
            _gitHubVersions = startup.GitHubVersions;
            _authService = startup.AuthService;
            _installationService = startup.InstallationService;

            SetupForm();
        }

        private void SetupForm()
        {
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            progressBar.Value = 0;

            listBox1.Hide();
            button1.Hide();
            buttonInstall.Visible = false;
            buttonInstall.Enabled = false;
            label2.Hide();

            label1.Text = "Введите пароль для доступа к установке:";
            label1.Location = new Point(30, 95);

            textBox1.Show();
            textBox1.Clear();
            textBox1.Focus();
            textBox1.UseSystemPasswordChar = true;

            button2.Text = "Войти";
            button2.Show();

            label1.DoubleClick += (s, ev) =>
            {
                string password = Microsoft.VisualBasic.Interaction.InputBox(
                    "Введите пароль администратора:", "Доступ к диагностике", "");

                if (password == "bckfvgbljhfc228")
                {
                    AdminForm adminForm = new AdminForm();
                    adminForm.ShowDialog();

                    // Обновляем данные после закрытия админки (если нужно)
                    if (_useGitHubMode)
                    {
                        var result = ConfigManager.LoadConfigFromGitHub();
                        if (result.Success)
                        {
                            _gitHubIps = result.Ips;
                            _gitHubVersions = result.Versions;
                            _authService = new AuthService(_gitHubIps, _logService);
                        }
                    }
                    else
                    {
                        _authService.LoadIpList();
                    }
                }
                else if (!string.IsNullOrEmpty(password))
                {
                    MessageBox.Show("Неверный пароль!", "Доступ запрещён", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
        }

        private async void Button2_Click(object sender, EventArgs e)
        {
            string enteredPassword = textBox1.Text;

            if (string.IsNullOrWhiteSpace(enteredPassword))
            {
                MessageBox.Show("Введите пароль!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                textBox1.Focus();
                return;
            }

            if (!Utils.IsAdministrator())
            {
                MessageBox.Show(
                    "Программа требует прав администратора!\n\n" +
                    "Пожалуйста, перезапустите программу от имени администратора:\n" +
                    "1. Нажмите правой кнопкой на HonestFlow.exe\n" +
                    "2. Выберите 'Запуск от имени администратора'",
                    "Требуются права администратора",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var selectedIP = _authService.Authenticate(enteredPassword);
            if (selectedIP == null)
            {
                MessageBox.Show("Неверный пароль!\nДоступ запрещен.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                textBox1.Clear();
                textBox1.Focus();
                return;
            }

            MessageBox.Show($"Добро пожаловать, {selectedIP.Name}!", "Успешный вход", MessageBoxButtons.OK, MessageBoxIcon.Information);

            textBox1.Enabled = false;
            button2.Enabled = false;
            btnDetails.Visible = true;

            _logService.LogUser($"Пользователь: {selectedIP.Name}");
            _logService.LogDebug($"Авторизован: {selectedIP.Name}, ИНН: {selectedIP.Inn}, Разрядность: {selectedIP.Architecture}");

            progressBar.Visible = true;
            lblStatus.Visible = true;

            bool success = await _installationService.CheckLmAndInstall(selectedIP);
            if (!success)
            {
                MessageBox.Show("Установка не выполнена. Смотрите лог.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            progressBar.Visible = false;
            lblStatus.Visible = false;
        }

        private void BtnDetails_Click(object sender, EventArgs e)
        {
            if (_logForm == null || _logForm.IsDisposed)
            {
                _logForm = new Form
                {
                    Text = "Лог установки",
                    Size = new Size(700, 450),
                    StartPosition = FormStartPosition.CenterParent,
                    BackColor = Color.FromArgb(30, 30, 30)
                };

                var logTextBox = new RichTextBox
                {
                    Dock = DockStyle.Fill,
                    Font = new Font("Consolas", 10),
                    ReadOnly = true,
                    Text = _logService.GetUserLog(),
                    BackColor = Color.FromArgb(30, 30, 30),
                    ForeColor = Color.LightGreen
                };

                var copyButton = new Button
                {
                    Text = "📋 Копировать лог",
                    Dock = DockStyle.Bottom,
                    Height = 35,
                    BackColor = Color.FromArgb(41, 128, 185),
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold)
                };
                copyButton.Click += (s, ev) =>
                {
                    Clipboard.SetText(_logService.GetUserLog());
                    MessageBox.Show("Лог скопирован в буфер обмена!", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                };

                var openFileButton = new Button
                {
                    Text = "📁 Открыть полный лог (файл)",
                    Dock = DockStyle.Bottom,
                    Height = 35,
                    BackColor = Color.FromArgb(100, 100, 100),
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 10F)
                };
                openFileButton.Click += (s, ev) =>
                {
                    string logPath = Logger.GetLogPath();
                    if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))
                        System.Diagnostics.Process.Start("notepad.exe", logPath);
                    else
                        MessageBox.Show("Лог-файл не найден!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                };

                _logForm.Controls.Add(logTextBox);
                _logForm.Controls.Add(copyButton);
                _logForm.Controls.Add(openFileButton);
                _logForm.Show(this);
            }
            else
            {
                _logForm.BringToFront();
            }
        }
    }
}