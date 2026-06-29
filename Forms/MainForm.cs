using HonestFlow.Application.Bootstrap;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Models;
using HonestFlow.Services.Auth;
using HonestFlow.Services.Core;
using HonestFlow.Services.Diagnostics;
using HonestFlow.Services.Installation;
using HonestFlow.Services.PointStatus;
using HonestFlow.Services.Ui;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

        private readonly DiagnosticArchiveService _diagnosticArchiveService;
        private readonly DiagnosticsEmailSender _diagnosticsEmailSender;
        private readonly WindowsServiceControlService _serviceControlService;
        private readonly ExternalApplicationLauncher _externalApplicationLauncher;
        private readonly WindowIconService _windowIconService;
        private readonly IUserDialogService _dialogService;
        private PointStatusService _pointStatusService;
        private bool _statusRefreshRunning;
        private bool _serviceActionRunning;

        private static readonly Color StatusGreen = Color.FromArgb(34, 197, 94);
        private static readonly Color StatusYellow = Color.FromArgb(251, 191, 36);
        private static readonly Color StatusRed = Color.FromArgb(239, 68, 68);
        private static readonly Color StatusGray = Color.FromArgb(148, 163, 184);

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
            _diagnosticsEmailSender = new DiagnosticsEmailSender(_logService, new AnyDeskIdProvider());
            _serviceControlService = new WindowsServiceControlService();
            _externalApplicationLauncher = new ExternalApplicationLauncher();
            _windowIconService = new WindowIconService(_logService);

            var startup = new ApplicationStartupService(_logService, _progressService, _dialogService).Start();
            _useGitHubMode = startup.UseGitHubMode;
            _gitHubIps = startup.GitHubIps;
            _gitHubVersions = startup.GitHubVersions;
            _authService = startup.AuthService;
            _installationService = startup.InstallationService;
            _pointStatusService = new PointStatusService(_useGitHubMode, _gitHubIps?.Count ?? 0);

            InitializeUiState();
            WireUiEvents();
            _windowIconService.ApplyExecutableIcon(this);
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
            button.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
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
            actionButton.Click += new System.EventHandler(this.BtnRefreshStatus_Click);

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
            lblGithubNode.Text = "Связь с облаком";
            SetNodeChecking();
        }

        private void WireUiEvents()
        {
            btnCheckWithoutPassword.Text = "Обновить статусы";
            btnCheckWithoutPassword.Click -= BtnDiagnostics_Click;
            btnCheckWithoutPassword.Click += BtnRefreshStatus_Click;

            btnDiagnostics.Text = "Собрать диагностику";
            btnDiagnostics.Visible = true;
            btnDiagnostics.Click -= BtnDiagnostics_Click;
            btnDiagnostics.Click += BtnDiagnostics_Click;

            btnOpenKktDriver.Click += BtnOpenKktDriver_Click;
            btnOpenEsm.Click += BtnOpenEsm_Click;

            Shown += async (s, e) => await RefreshPointStatusAsync();
        }

        private void BtnOpenKktDriver_Click(object sender, EventArgs e)
        {
            OpenExternalApplication(() => _externalApplicationLauncher.OpenKktDriver(), "Драйвер ККТ");
        }

        private void BtnOpenEsm_Click(object sender, EventArgs e)
        {
            OpenExternalApplication(() => _externalApplicationLauncher.OpenEsm(), "ЕСМ");
        }

        private void OpenExternalApplication(Action open, string title)
        {
            try
            {
                open();
                lblStatus.Text = $"Открыто: {title}";
            }
            catch (FileNotFoundException ex)
            {
                MessageBox.Show(
                    $"Не найден файл:\n{ex.FileName}",
                    title,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Не удалось открыть {title}:\n{ex.Message}",
                    title,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
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

        private async void BtnDiagnostics_Click(object sender, EventArgs e)
        {
            string archivePath = null;

            try
            {
                btnDiagnostics.Enabled = false;
                progressBar.Visible = true;
                progressBar.Value = 0;
                lblStatus.Text = "Сборка архива диагностики...";

                archivePath = await Task.Run(() => _diagnosticArchiveService.CreateArchive());
                progressBar.Value = 35;
                lblStatus.Text = $"Архив собран: {Path.GetFileName(archivePath)}";

                var sendConfirm = MessageBox.Show(
                    "Диагностический архив собран.\nОтправить его на электронную почту?",
                    "Отправка диагностики",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (sendConfirm != DialogResult.Yes)
                {
                    Process.Start(
                        "explorer.exe",
                        $"/select,\"{archivePath}\"");

                    lblStatus.Text = "Архив диагностики собран";
                    progressBar.Value = 100;
                    return;
                }

                await _diagnosticsEmailSender.SendWithRetries(archivePath, SetDiagnosticsProgress);

                MessageBox.Show(
                    $"Диагностический архив создан и отправлен:\n{archivePath}",
                    "Диагностика",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                progressBar.Value = 100;
                lblStatus.Text = "Диагностика отправлена";
            }
            catch (Exception ex)
            {
                _logService.LogDebug($"Ошибка сбора диагностики: {ex.Message}");

                if (!string.IsNullOrWhiteSpace(archivePath) && File.Exists(archivePath))
                {
                    Process.Start(
                        "explorer.exe",
                        $"/select,\"{archivePath}\"");
                }

                MessageBox.Show(
                    $"Не удалось завершить диагностику:\n{ex.Message}" +
                    (string.IsNullOrWhiteSpace(archivePath) ? string.Empty : $"\n\nАрхив сохранён локально:\n{archivePath}"),
                    "Ошибка диагностики",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                lblStatus.Text = $"Ошибка диагностики: {ex.Message}";
            }
            finally
            {
                btnDiagnostics.Enabled = true;
            }
        }

        private void SetDiagnosticsProgress(int progress, string message)
        {
            progressBar.Value = Math.Min(Math.Max(progress, progressBar.Minimum), progressBar.Maximum);
            lblStatus.Text = message;
        }

        private async void BtnRefreshStatus_Click(object sender, EventArgs e)
        {
            await RefreshPointStatusAsync();
        }

        private async void ServiceAction_Click(object sender, EventArgs e)
        {
            if (_serviceActionRunning)
                return;

            if (!(sender is Button button) || !(button.Tag is NodeStatus status))
                return;

            if (!status.CanManageServices)
            {
                ShowNodeDetails(status);
                return;
            }

            if (!Utils.IsAdministrator())
            {
                MessageBox.Show(
                    "Для управления службами нужны права администратора.\nПерезапустите HonestFlow от имени администратора.",
                    "Нужны права администратора",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            bool shouldStart = status.Services.Any(x => !x.IsRunning);
            string actionName = shouldStart ? "запустить" : "перезапустить";
            string serviceList = string.Join(", ", status.Services.Select(x => x.ServiceName));

            if (!shouldStart)
            {
                var confirm = MessageBox.Show(
                    $"Перезапустить службы?\n\n{serviceList}",
                    "Перезапуск служб",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (confirm != DialogResult.Yes)
                    return;
            }

            try
            {
                _serviceActionRunning = true;
                button.Enabled = false;
                btnCheckWithoutPassword.Enabled = false;
                lblStatus.Text = $"Пытаюсь {actionName} службы: {serviceList}";

                if (shouldStart)
                    await Task.Run(() => _serviceControlService.StartStoppedServices(status.Services));
                else
                    await Task.Run(() => _serviceControlService.RestartServices(status.Services));

                lblStatus.Text = "Операция со службами завершена";
                await RefreshPointStatusAsync();
            }
            catch (Exception ex)
            {
                _logService.LogDebug($"Ошибка управления службами: {ex.Message}");
                MessageBox.Show(
                    $"Не удалось {actionName} службы:\n{ex.Message}",
                    "Ошибка служб",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                lblStatus.Text = $"Ошибка служб: {ex.Message}";
            }
            finally
            {
                button.Enabled = true;
                btnCheckWithoutPassword.Enabled = true;
                _serviceActionRunning = false;
            }
        }

        private async Task RefreshPointStatusAsync()
        {
            if (_statusRefreshRunning)
                return;

            _statusRefreshRunning = true;
            btnCheckWithoutPassword.Enabled = false;
            SetNodeChecking();
            lblStatus.Text = "Проверка служб и связи...";
            lblHeaderStatus.Text = "● Проверка";
            lblHeaderStatus.ForeColor = StatusYellow;

            try
            {
                var result = await Task.Run(() => _pointStatusService.Check());

                ApplyNodeStatus(lblLmCircle, btnLmAction, result.Lm);
                ApplyNodeStatus(lblControllerCircle, btnControllerAction, result.Controller);
                ApplyNodeStatus(lblEsmCircle, btnEsmAction, result.Esm);
                ApplyNodeStatus(lblKktCircle, btnKktAction, result.Kkt);
                ApplyNodeStatus(lblGithubCircle, btnGithubAction, result.Cloud);

                bool hasRed = new[] { result.Lm, result.Controller, result.Esm, result.Kkt, result.Cloud }
                    .Any(x => x.Level == NodeLevel.Error);
                bool hasYellow = new[] { result.Lm, result.Controller, result.Esm, result.Kkt, result.Cloud }
                    .Any(x => x.Level == NodeLevel.Warning);

                lblHeaderStatus.Text = hasRed
                    ? "● Есть проблемы"
                    : hasYellow ? "● Требует внимания" : "● Всё работает";
                lblHeaderStatus.ForeColor = hasRed ? StatusRed : hasYellow ? StatusYellow : StatusGreen;
                lblStatus.Text = "Проверка завершена";
            }
            catch (Exception ex)
            {
                _logService.LogDebug($"Ошибка проверки состояния точки: {ex.Message}");
                lblStatus.Text = $"Ошибка проверки: {ex.Message}";
                lblHeaderStatus.Text = "● Ошибка проверки";
                lblHeaderStatus.ForeColor = StatusRed;
            }
            finally
            {
                btnCheckWithoutPassword.Enabled = true;
                _statusRefreshRunning = false;
            }
        }

        private void SetNodeChecking()
        {
            SetNodeChecking(lblLmCircle, btnLmAction);
            SetNodeChecking(lblControllerCircle, btnControllerAction);
            SetNodeChecking(lblEsmCircle, btnEsmAction);
            SetNodeChecking(lblKktCircle, btnKktAction);
            SetNodeChecking(lblGithubCircle, btnGithubAction);
        }

        private void SetNodeChecking(Label circle, Button actionButton)
        {
            SetNode(circle, actionButton, StatusGray, "Проверка");
            actionButton.Tag = null;
            actionButton.Click -= ShowNodeDetails_Click;
            actionButton.Click -= ServiceAction_Click;
            actionButton.Click -= BtnRefreshStatus_Click;
            actionButton.Click += BtnRefreshStatus_Click;
        }

        private void ApplyNodeStatus(Label circle, Button actionButton, NodeStatus status)
        {
            Color color = status.Level switch
            {
                NodeLevel.Ok => StatusGreen,
                NodeLevel.Warning => StatusYellow,
                NodeLevel.Error => StatusRed,
                _ => StatusGray
            };

            SetNode(circle, actionButton, color, status.ShortText);
            actionButton.Text = status.ActionText;
            actionButton.Tag = status;
            actionButton.Click -= BtnRefreshStatus_Click;
            actionButton.Click -= ShowNodeDetails_Click;
            actionButton.Click -= ServiceAction_Click;
            actionButton.Click += status.CanManageServices ? ServiceAction_Click : ShowNodeDetails_Click;
        }

        private static void SetNode(Label circle, Button actionButton, Color color, string text)
        {
            circle.Text = "●";
            circle.ForeColor = color;
            actionButton.Text = text;
        }

        private void ShowNodeDetails_Click(object sender, EventArgs e)
        {
            if (sender is Button button && button.Tag is NodeStatus status)
                ShowNodeDetails(status);
        }

        private static void ShowNodeDetails(NodeStatus status)
        {
            if (!string.IsNullOrWhiteSpace(status?.Details))
                MessageBox.Show(status.Details, "Состояние точки", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BtnDetails_Click(object sender, EventArgs e)
        {
            try
            {
                string logPath = Logger.GetLogPath();
                if (!string.IsNullOrWhiteSpace(logPath) && File.Exists(logPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = logPath,
                        UseShellExecute = true
                    });
                    return;
                }

                string logsFolder = Logger.GetLogsFolder();
                if (Directory.Exists(logsFolder))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = logsFolder,
                        UseShellExecute = true
                    });
                    return;
                }

                MessageBox.Show("Файл лога не найден.", "Журнал выполнения", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Не удалось открыть журнал:\n{ex.Message}",
                    "Журнал выполнения",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
