using HonestFlow.Application.Bootstrap;
using HonestFlow.Forms;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Models;
using HonestFlow.Services.Auth;
using HonestFlow.Services.Core;
using HonestFlow.Services.Diagnostics;
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
        private readonly DiagnosticArchiveService _diagnosticArchiveService;
        private readonly IUserDialogService _dialogService;

        public MainForm()
        {
            InitializeComponent();
            //this.DoubleBuffered = true;
            //this.SetStyle(
            //    ControlStyles.AllPaintingInWmPaint |
            //    ControlStyles.UserPaint |
            //    ControlStyles.OptimizedDoubleBuffer,
            //    true);

            //this.UpdateStyles();

            _logService = new LogService();
            _progressService = new ProgressService(progressBar, lblStatus);
            _dialogService = new WinFormsDialogService(this);
            _diagnosticArchiveService = new DiagnosticArchiveService(_logService);

            var startup = new ApplicationStartupService(_logService, _progressService, _dialogService).Start();
            _useGitHubMode = startup.UseGitHubMode;
            _gitHubIps = startup.GitHubIps;
            _gitHubVersions = startup.GitHubVersions;
            _authService = startup.AuthService;
            _installationService = startup.InstallationService;

            InitializeUiState();
            WireUiEvents();
        }

        public void ConfigureButton(System.Windows.Forms.Button button, string text, bool primary)
        {
            button.BackColor = primary
                ? System.Drawing.Color.FromArgb(37, 99, 235)
                : System.Drawing.Color.White;

            button.Cursor = System.Windows.Forms.Cursors.Hand;
            button.Dock = System.Windows.Forms.DockStyle.Fill;
            button.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            button.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(180, 190, 205);
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            button.ForeColor = primary
                ? System.Drawing.Color.White
                : System.Drawing.Color.FromArgb(30, 41, 59);
            button.Margin = new System.Windows.Forms.Padding(0, 7, 0, 7);
            button.Text = text;
            button.UseVisualStyleBackColor = false;
        }

        public void ConfigureNodeRow(
            int row,
            System.Windows.Forms.Label nodeLabel,
            System.Windows.Forms.Label statusCircle,
            System.Windows.Forms.Button actionButton,
            string nodeText,
            System.Drawing.Color circleColor,
            string actionText)
        {
            nodeLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            nodeLabel.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold);
            nodeLabel.ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);
            nodeLabel.Padding = new System.Windows.Forms.Padding(12, 0, 0, 0);
            nodeLabel.Text = nodeText;
            nodeLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            statusCircle.Dock = System.Windows.Forms.DockStyle.Fill;
            statusCircle.Font = new System.Drawing.Font("Segoe UI", 20F, System.Drawing.FontStyle.Bold);
            statusCircle.ForeColor = circleColor;
            statusCircle.Text = "●";
            statusCircle.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;

            actionButton.Dock = System.Windows.Forms.DockStyle.Fill;
            actionButton.Margin = new System.Windows.Forms.Padding(12, 10, 12, 10);
            actionButton.BackColor = System.Drawing.Color.White;
            actionButton.Cursor = System.Windows.Forms.Cursors.Hand;
            actionButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            actionButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(180, 190, 205);
            actionButton.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            actionButton.ForeColor = System.Drawing.Color.FromArgb(30, 41, 59);
            actionButton.Text = actionText;
            actionButton.UseVisualStyleBackColor = false;
            actionButton.Click += new System.EventHandler(this.BtnDiagnostics_Click);

            this.nodeTable.Controls.Add(nodeLabel, 0, row);
            this.nodeTable.Controls.Add(statusCircle, 1, row);
            this.nodeTable.Controls.Add(actionButton, 2, row);
        }

        private void InitializeUiState()
        {
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            progressBar.Value = 0;

            textBox1.Clear();
            textBox1.UseSystemPasswordChar = true;

            lblStatus.Text = "Ожидание запуска проверки";
            lblHeaderStatus.Text = "● Ожидание проверки";
        }

        private void WireUiEvents()
        {
            btnDiagnostics.Text = "Диагностика и ремонт";
            btnDiagnostics.Visible = true;
            btnDiagnostics.Click -= BtnDiagnostics_Click;
            btnDiagnostics.Click += BtnDiagnostics_Click;
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

        private void BtnDiagnostics_Click(object sender, EventArgs e)
        {
            using var form = new ServiceMenuForm();
            form.ShowDialog(this);
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
