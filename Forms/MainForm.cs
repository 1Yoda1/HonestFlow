using HonestFlow.Application.Bootstrap;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Models;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Core;
using HonestFlow.Application.Diagnostics;
using HonestFlow.Application.Feedback;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Installation.Planning;
using HonestFlow.Application.Lm;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.PointStatus;
using HonestFlow.Application.PointIdentity;
using HonestFlow.Application.RemoteAccess;
using HonestFlow.Application.Ui;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Infrastructure.Composition;
using HonestFlow.Models.Licensing;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HonestFlow
{
    public partial class MainForm : Form, IUserNotificationSink
    {
        private readonly ILogService _logService;
        private readonly SellerAuthenticationWorkflow _sellerAuthenticationWorkflow;
        private readonly ComponentInstallationWorkflow _componentInstallationWorkflow;
        private readonly MaintenanceWorkflow _maintenanceWorkflow;
        private VersionsData _remoteVersions;
        private IPData _selectedIP;

        private readonly DiagnosticWorkflowService _diagnosticWorkflowService;
        private readonly RuDesktopWorkflow _ruDesktopWorkflow;
        private readonly HelpRequestWorkflow _helpRequestWorkflow;
        private readonly AppRatingEmailSender _appRatingEmailSender;
        private readonly PointRepairWorkflow _pointRepairWorkflow;
        private readonly PointStatusReportBuilder _pointStatusReportBuilder;
        private readonly ExternalApplicationLauncher _externalApplicationLauncher;
        private readonly WindowIconService _windowIconService;
        private readonly MainFormDialogService _mainFormDialogs;
        private readonly PointStatusRefreshService _pointStatusRefreshService;
        private PointStatusResult _lastPointStatusResult;
        private bool _statusRefreshRunning;
        private bool _statusRefreshAfterLicenseChangePending;
        private bool _serviceActionRunning;
        private string _longOperationName;
        private LicenseObservationSnapshot _lastPresentedLicenseSnapshot;
        private readonly ILicenseObservationSnapshotStore _licenseSnapshotStore;
        private readonly ILicenseAccessPolicy _licenseAccessPolicy;
        private readonly LicensePresentationService _licensePresentationService;
        private readonly LicenseRefreshWorkflow _licenseRefreshWorkflow;
        private readonly ToolTip _licenseToolTip = new();
        private readonly System.Windows.Forms.Timer _notificationTimer = new();
        private readonly DeviceRegistrationWorkflow _deviceRegistrationWorkflow;
        private readonly IPData _startupAuthorizedClient;
        private readonly bool _startupAuthenticationHandled;
        private readonly CancellationTokenSource _lifetimeCancellation = new();
        private CancellationTokenSource _installationCancellation;

        private static readonly Color StatusGreen = Color.FromArgb(34, 197, 94);
        private static readonly Color StatusYellow = Color.FromArgb(251, 191, 36);
        private static readonly Color StatusRed = Color.FromArgb(239, 68, 68);
        private static readonly Color StatusGray = Color.FromArgb(148, 163, 184);

        public MainForm()
            : this(null)
        {
        }

        public MainForm(StartupResult startup)
        {
            InitializeComponent();
            //this.DoubleBuffered = true;
            //this.SetStyle(
            //    ControlStyles.AllPaintingInWmPaint |
            //    ControlStyles.UserPaint |
            //    ControlStyles.OptimizedDoubleBuffer,
            //    true);

            //this.UpdateStyles();

            MainFormDependencies dependencies = MainFormCompositionRoot.Create(
                this,
                progressBar,
                lblStatus,
                startup,
                () => _selectedIP?.ClientId);
            startup = dependencies.Startup;
            _logService = dependencies.LogService;
            _mainFormDialogs = dependencies.MainFormDialogService;
            _ruDesktopWorkflow = dependencies.RuDesktopWorkflow;
            _diagnosticWorkflowService = dependencies.DiagnosticWorkflowService;
            _helpRequestWorkflow = dependencies.HelpRequestWorkflow;
            _appRatingEmailSender = dependencies.AppRatingEmailSender;
            _externalApplicationLauncher = dependencies.ExternalApplicationLauncher;
            _windowIconService = dependencies.WindowIconService;
            _deviceRegistrationWorkflow = dependencies.DeviceRegistrationWorkflow;
            _licenseSnapshotStore = dependencies.LicenseSnapshotStore;
            _licenseAccessPolicy = dependencies.LicenseAccessPolicy;
            _licensePresentationService = dependencies.LicensePresentationService;
            _licenseRefreshWorkflow = dependencies.LicenseRefreshWorkflow;
            _sellerAuthenticationWorkflow = dependencies.SellerAuthenticationWorkflow;
            _pointRepairWorkflow = dependencies.PointRepairWorkflow;
            _pointStatusReportBuilder = dependencies.PointStatusReportBuilder;
            _componentInstallationWorkflow = dependencies.ComponentInstallationWorkflow;
            _maintenanceWorkflow = dependencies.MaintenanceWorkflow;
            _pointStatusRefreshService = dependencies.PointStatusRefreshService;
            _licenseSnapshotStore.SnapshotChanged += LicenseSnapshotChanged;
            _notificationTimer.Tick += (_, _) => ClearTransientNotification();
            FormClosed += (_, _) =>
            {
                _lifetimeCancellation.Cancel();
                _notificationTimer.Stop();
                _licenseSnapshotStore.SnapshotChanged -= LicenseSnapshotChanged;
            };

            _remoteVersions = startup.RemoteVersions;
            _startupAuthorizedClient = startup.AuthorizedClient;
            _startupAuthenticationHandled = startup.SellerAuthenticationHandled;
            InitializeUiState();
            WireUiEvents();
            ApplyLicenseAccessToUi();
            _windowIconService.ApplyExecutableIcon(this);
        }

        public void ShowNotification(
            string message,
            string title,
            UserNotificationSeverity severity)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => ShowNotification(message, title, severity)));
                return;
            }

            _notificationTimer.Stop();
            string prefix = severity switch
            {
                UserNotificationSeverity.Success => "✓ ",
                UserNotificationSeverity.Warning => "⚠ ",
                UserNotificationSeverity.Error => "✕ ",
                _ => "• "
            };
            lblStatus.Text = prefix + message.Replace(Environment.NewLine, " ");
            lblStatus.Visible = true;
            lblStatus.ForeColor = severity switch
            {
                UserNotificationSeverity.Success => Color.FromArgb(22, 163, 74),
                UserNotificationSeverity.Warning => Color.FromArgb(180, 83, 9),
                UserNotificationSeverity.Error => Color.FromArgb(220, 38, 38),
                _ => Color.FromArgb(37, 99, 235)
            };
            _licenseToolTip.SetToolTip(
                lblStatus,
                string.IsNullOrWhiteSpace(title) ? message : $"{title}: {message}");

            _notificationTimer.Interval = severity switch
            {
                UserNotificationSeverity.Error => 12000,
                UserNotificationSeverity.Warning => 9000,
                _ => 5000
            };
            _notificationTimer.Start();
        }

        private void ClearTransientNotification()
        {
            _notificationTimer.Stop();
            lblStatus.ForeColor = Color.FromArgb(51, 65, 85);
            _licenseToolTip.SetToolTip(lblStatus, string.Empty);
        }

        private void ShowInlineWarning(string message, string title = null) =>
            ShowNotification(message, title, UserNotificationSeverity.Warning);

        private void ShowInlineError(string message, string title = null) =>
            ShowNotification(message, title, UserNotificationSeverity.Error);

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
            System.Windows.Forms.Label statusTextLabel,
            System.Windows.Forms.Label statusCircle,
            System.Windows.Forms.Button actionButton,
            string nodeText,
            System.Drawing.Color circleColor,
            string actionText)
        {
            nodeLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            nodeLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
            nodeLabel.ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);
            nodeLabel.Padding = new System.Windows.Forms.Padding(12, 0, 0, 0);
            nodeLabel.Text = nodeText;
            nodeLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            statusTextLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            statusTextLabel.AutoEllipsis = true;
            statusTextLabel.Font = new System.Drawing.Font("Segoe UI", 9.25F);
            statusTextLabel.ForeColor = System.Drawing.Color.FromArgb(51, 65, 85);
            statusTextLabel.Padding = new System.Windows.Forms.Padding(8, 4, 8, 4);
            statusTextLabel.Text = "Ожидание";
            statusTextLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            statusCircle.Dock = System.Windows.Forms.DockStyle.Fill;
            statusCircle.Font = new System.Drawing.Font("Segoe UI", 18F, System.Drawing.FontStyle.Bold);
            statusCircle.ForeColor = circleColor;
            statusCircle.Text = "●";
            statusCircle.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;

            actionButton.Dock = System.Windows.Forms.DockStyle.Fill;
            actionButton.Margin = new System.Windows.Forms.Padding(8, 14, 8, 14);
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
            this.nodeTable.Controls.Add(statusTextLabel, 1, row);
            this.nodeTable.Controls.Add(statusCircle, 2, row);
            this.nodeTable.Controls.Add(actionButton, 3, row);
        }

        private void InitializeUiState()
        {
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            progressBar.Value = 0;

            textBox1.Clear();
            textBox1.UseSystemPasswordChar = true;
            textBox1.Visible = false;
            label1.Visible = false;
            lblAuthTitle.Text = "Доступ к точке";
            label1.Text = "Пароль продавца";
            button2.Text = "Войти как продавец";
            button2.Visible = true;
            leftLayout.RowStyles[1].Height = 40;
            leftLayout.RowStyles[2].Height = 0;
            leftLayout.RowStyles[3].Height = 0;
            btnStartInstallation.Visible = false;
            btnMaintenance.Visible = false;
            btnRateApplication.Visible = false;
            lblRatingThanks.Visible = false;
            btnRefreshLicense.Enabled = false;
            btnRefreshLicense.Visible = false;
            leftLayout.RowStyles[9].Height = 0;
            leftLayout.RowStyles[10].Height = 0;

            lblStatus.Text = "Ожидание запуска проверки";
            lblHeaderStatus.Text = "● Ожидание проверки";
            lblAuthorizedClient.Text = "Продавец не авторизован";
            lblCloudNode.Text = "Связь с облаком";
            SetNodeChecking(canViewAndRepair: false);
        }

        private void WireUiEvents()
        {
            btnCheckWithoutPassword.Text = "Обновить статусы";
            btnCheckWithoutPassword.Click -= BtnDiagnostics_Click;
            btnCheckWithoutPassword.Click += BtnRefreshStatus_Click;
            btnPointStatusDetails.Click += ShowPointStatusDetails_Click;

            btnDiagnostics.Text = "Собрать диагностику";
            btnDiagnostics.Visible = true;
            btnDiagnostics.Click -= BtnDiagnostics_Click;
            btnDiagnostics.Click += BtnDiagnostics_Click;

            btnReinstallComponents.Click += BtnReinstallComponents_Click;
            btnRestoreLmDatabase.Click += BtnRestoreLmDatabase_Click;
            btnMaintenance.Click += BtnMaintenance_Click;
            btnStartInstallation.Click += BtnStartInstallation_Click;

            btnOpenKktDriver.Click += BtnOpenKktDriver_Click;
            btnOpenEsm.Click += BtnOpenEsm_Click;
            btnRateApplication.Click += BtnRateApplication_Click;
            btnRefreshLicense.Click += BtnRefreshLicense_Click;

            Shown += MainForm_Shown;
        }

        private async void MainForm_Shown(object sender, EventArgs e)
        {
            if (_startupAuthorizedClient != null)
            {
                ApplyAuthorizedClient(_startupAuthorizedClient);
                await RefreshPointStatusAsync();
            }
            else if (!_startupAuthenticationHandled)
            {
                await PromptSellerLoginAsync();
            }

            _ = RunPeriodicLicenseRefreshAsync(_lifetimeCancellation.Token);
        }

        private async Task RunPeriodicLicenseRefreshAsync(CancellationToken cancellationToken)
        {
            await _licenseRefreshWorkflow.RunPeriodicAsync(() => _selectedIP, cancellationToken);
        }

        private async void BtnRefreshLicense_Click(object sender, EventArgs e)
        {
            if (_selectedIP == null)
            {
                ShowInlineWarning("Сначала войдите как продавец.", "Обновление лицензии");
                return;
            }

            if (!_licenseRefreshWorkflow.IsAvailable)
            {
                ShowInlineWarning("Повторная проверка лицензии недоступна в текущем режиме.", "Обновление лицензии");
                return;
            }

            if (!EnsureNoLongOperation("обновление лицензии"))
                return;

            try
            {
                btnRefreshLicense.Enabled = false;
                progressBar.Visible = true;
                progressBar.Style = ProgressBarStyle.Marquee;
                lblStatus.Text = "Обновляем сведения о лицензии...";

                var progress = new Progress<LicenseAuthenticationProgress>(value =>
                {
                    lblStatus.Text = value.Stage switch
                    {
                        LicenseAuthenticationStage.CheckingDeviceAndLicense =>
                            "Получаем и проверяем лицензию...",
                        LicenseAuthenticationStage.Completed =>
                            "Сведения о лицензии обновлены.",
                        _ => "Обновляем сведения о лицензии..."
                    };
                });

                LicenseObservationSnapshot snapshot = await _licenseRefreshWorkflow.RefreshAsync(
                    _selectedIP,
                    progress,
                    CancellationToken.None);
                Logger.Info(
                    $"Event=ManualLicenseRefresh Decision={snapshot?.Decision} " +
                    $"TechnicalCode={snapshot?.TechnicalCode}",
                    nameof(MainForm));
                lblStatus.Text = snapshot?.Decision == LicenseDecision.Allowed
                    ? "Лицензия обновлена и действительна."
                    : $"Лицензия обновлена: {snapshot?.Message ?? "состояние не определено"}";
            }
            catch (Exception ex)
            {
                Logger.Warning(
                    $"Event=ManualLicenseRefresh Status=Failed ErrorType={ex.GetType().Name}",
                    nameof(MainForm));
                _logService.LogDebug($"Ошибка ручного обновления лицензии: {ex}");
                lblStatus.Text = "Не удалось обновить сведения о лицензии.";
                ShowInlineError("Не удалось обновить лицензию. Проверьте подключение к интернету.", "Обновление лицензии");
            }
            finally
            {
                progressBar.Style = ProgressBarStyle.Blocks;
                progressBar.Value = 0;
                btnRefreshLicense.Enabled = _selectedIP != null;
            }
        }

        private void BtnOpenKktDriver_Click(object sender, EventArgs e)
        {
            OpenExternalApplication(() => _externalApplicationLauncher.OpenKktDriver(), "Драйвер ККТ");
        }

        private void BtnOpenEsm_Click(object sender, EventArgs e)
        {
            OpenExternalApplication(() => _externalApplicationLauncher.OpenEsm(), "ЕСМ");
        }

        private async void BtnRateApplication_Click(object sender, EventArgs e)
        {
            if (_selectedIP == null)
            {
                ShowInlineWarning("Сначала войдите как продавец.", "Оценить HonestFlow");
                return;
            }

            try
            {
                btnRateApplication.Enabled = false;
                lblStatus.Text = "Отправка оценки HonestFlow...";

                string pointAddress = _mainFormDialogs.ResolveCurrentPointAddress().Address;
                await _appRatingEmailSender.Send(_selectedIP.Name, pointAddress);

                btnRateApplication.Visible = false;
                leftLayout.RowStyles[9].Height = 0;
                leftLayout.RowStyles[10].Height = 40;
                lblRatingThanks.Visible = true;
                lblStatus.Text = "Спасибо за оценку <3";
            }
            catch (Exception ex)
            {
                _logService.LogDebug($"Ошибка отправки оценки HonestFlow: {ex.Message}");
                ShowInlineError($"Не удалось отправить оценку: {ex.Message}", "Оценить HonestFlow");
                lblStatus.Text = "Не удалось отправить оценку";
            }
            finally
            {
                if (btnRateApplication.Visible)
                    btnRateApplication.Enabled = true;
            }
        }

        private void OpenExternalApplication(Action open, string title)
        {
            LogOperatorAction($"открытие внешнего приложения: {title}");

            if (!EnsureLicenseAccess(LicenseOperation.OpenLocalTools, $"открытие {title}"))
                return;

            if (!EnsureNoLongOperation($"открытие {title}"))
                return;

            try
            {
                open();
                lblStatus.Text = $"Открыто: {title}";
                LogOperatorAction($"внешнее приложение открыто: {title}");
            }
            catch (FileNotFoundException ex)
            {
                LogOperatorAction($"не удалось открыть {title}: файл не найден", isError: true);
                _logService.LogDebug($"Не найден файл для запуска {title}: {ex.FileName}");

                ShowInlineWarning($"Не найден файл: {ex.FileName}", title);
            }
            catch (Exception ex)
            {
                LogOperatorAction($"не удалось открыть {title}: {ex.Message}", isError: true);
                _logService.LogDebug($"Ошибка запуска {title}: {ex}");

                ShowInlineError($"Не удалось открыть {title}: {ex.Message}", title);
            }
        }

        private async void Button2_Click(object sender, EventArgs e)
        {
            await PromptSellerLoginAsync();
        }

        private async Task PromptSellerLoginAsync()
        {
            if (_selectedIP != null)
                return;

            using var loginForm = new SellerLoginForm();
            if (loginForm.ShowDialog(this) != DialogResult.OK)
            {
                lblStatus.Text = "Вход продавца не выполнен. Доступен диагностический режим.";
                return;
            }

            LogOperatorAction("нажата кнопка входа");

            if (!EnsureNoLongOperation("вход"))
                return;

            string enteredPassword = loginForm.Password;

            LicenseAuthenticationResult authentication = await AuthenticateWithLicenseAsync(enteredPassword);
            var selectedIP = authentication.Client;
            if (selectedIP == null)
            {
                LogOperatorAction("вход отклонен: неверный пароль", isError: true);
                ShowInlineError("Неверный пароль. Доступ запрещён.", "Авторизация");
                return;
            }

            ApplyAuthorizedClient(selectedIP);
            await RefreshPointStatusAsync();
        }

        private async void BtnStartInstallation_Click(object sender, EventArgs e)
        {
            if (_installationCancellation != null)
            {
                if (!_installationCancellation.IsCancellationRequested)
                {
                    LogOperatorAction("пользователь запросил прерывание установки");
                    _installationCancellation.Cancel();
                    btnStartInstallation.Enabled = false;
                    btnStartInstallation.Text = "Прерывание...";
                    ShowNotification(
                        "Новые компоненты не запустятся. Если установщик уже работает, дождёмся его безопасного завершения.",
                        "Прерывание установки",
                        UserNotificationSeverity.Warning);
                }

                return;
            }

            await StartInstallationForAuthorizedUser();
        }

        private async Task<LicenseAuthenticationResult> AuthenticateWithLicenseAsync(string password)
        {
            if (!_sellerAuthenticationWorkflow.ReportsLicenseProgress)
            {
                return await _sellerAuthenticationWorkflow.AuthenticateAsync(
                    password,
                    null,
                    CancellationToken.None);
            }

            using var progressForm = new LicenseCheckProgressForm();
            var progress = new Progress<LicenseAuthenticationProgress>(progressForm.Report);
            progressForm.Show(this);
            progressForm.BringToFront();

            try
            {
                LicenseAuthenticationResult result = await _sellerAuthenticationWorkflow.AuthenticateAsync(
                    password,
                    progress,
                    CancellationToken.None);
                if (result.Client != null)
                {
                    progressForm.Complete(result);
                    await Task.Delay(650);
                }

                return result;
            }
            finally
            {
                progressForm.Close();
            }
        }

        private async Task StartInstallationForAuthorizedUser()
        {
            LogOperatorAction("нажата кнопка запуска проверки");

            IPData selectedIP = _selectedIP;
            ComponentOperationReadiness readiness = _componentInstallationWorkflow.CheckReadiness(
                selectedIP,
                checkLmRequirements: true);
            if (readiness.Status == ComponentOperationReadinessStatus.ClientRequired)
            {
                LogOperatorAction("запуск проверки отменен: пользователь не авторизован", isError: true);
                ShowInlineWarning("Сначала выполните вход.", "Авторизация");
                return;
            }

            if (readiness.Status == ComponentOperationReadinessStatus.AdministratorRequired)
            {
                LogOperatorAction("запуск проверки отменен: нет прав администратора", isError: true);
                MessageBox.Show(
                    "Программа требует прав администратора!\n\n" +
                    "Пожалуйста, перезапустите программу от имени администратора:\n" +
                    "1. Нажмите правой кнопкой на HonestFlow.exe\n" +
                    "2. Выберите 'Запуск от имени администратора'",
                    "Требуются права администратора",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            LmSystemRequirementsResult requirements = readiness.SystemRequirements;
            if (!requirements.MeetsMinimum)
            {
                ShowNotification(
                    "ПК ниже минимальных требований ЛМ ЧЗ. Установка продолжится, но возможна нестабильная работа:\n" +
                    string.Join("; ", requirements.MinimumWarnings),
                    "Системные требования ЛМ ЧЗ",
                    UserNotificationSeverity.Warning);
            }

            if (!TryBeginLongOperation(
                "проверка и установка компонентов",
                LicenseOperation.InstallComponents))
                return;

            _installationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCancellation.Token);
            btnStartInstallation.Text = "Прервать установку";
            btnStartInstallation.Enabled = true;
            progressBar.Visible = true;
            lblStatus.Visible = true;

            try
            {
                bool success = await _componentInstallationWorkflow.InstallAsync(
                    selectedIP,
                    _installationCancellation.Token);
                if (!success)
                {
                    LogOperatorAction("проверка и установка завершены с ошибкой", isError: true);
                    ShowInlineError("Установка не выполнена. Подробности записаны в журнал.", "Установка");
                }
                else
                {
                    LogOperatorAction("проверка и установка завершены успешно");
                }

                await RefreshPointStatusAsync(allowDuringLongOperation: true);
                ShowNotification(
                    success
                        ? "Установка завершена."
                        : "Установка не выполнена. Подробности записаны в журнал.",
                    "Установка",
                    success ? UserNotificationSeverity.Success : UserNotificationSeverity.Error);
            }
            catch (OperationCanceledException) when (_installationCancellation.IsCancellationRequested)
            {
                LogOperatorAction("установка прервана пользователем");
                await RefreshPointStatusAsync(allowDuringLongOperation: true);
                ShowNotification(
                    "Установка безопасно прервана после завершения текущего компонента.",
                    "Установка",
                    UserNotificationSeverity.Warning);
            }
            finally
            {
                _installationCancellation.Dispose();
                _installationCancellation = null;
                btnStartInstallation.Text = "Запустить установку";
                progressBar.Visible = false;
                EndLongOperation();
            }
        }

        private async void BtnDiagnostics_Click(object sender, EventArgs e)
        {
            LogOperatorAction("нажата кнопка сбора диагностики");

            if (!TryBeginLongOperation("сбор диагностики", LicenseOperation.CollectDiagnostics))
                return;

            DiagnosticArchiveInfo archiveInfo = null;

            try
            {
                DiagnosticLogSelection selection = _mainFormDialogs.ShowDiagnosticLogSelection();
                if (selection == null)
                {
                    LogOperatorAction("сбор диагностики отменен на выборе логов");
                    return;
                }

                LogOperatorAction($"запущен сбор диагностики: {DiagnosticWorkflowService.DescribeSelection(selection)}");
                btnDiagnostics.Enabled = false;
                progressBar.Visible = true;
                progressBar.Value = 0;
                lblStatus.Text = "Сборка архива диагностики...";

                string pointAddress = _mainFormDialogs.ResolvePointAddressForOnlineAction(
                    "Сбор диагностики",
                    "Для диагностического архива укажите адрес торговой точки.");

                archiveInfo = await _diagnosticWorkflowService.CreateArchiveAsync(
                    selection,
                    pointAddress,
                    _lifetimeCancellation.Token);
                string archivePath = archiveInfo.ArchivePath;
                progressBar.Value = 35;
                lblStatus.Text = $"Архив собран: {Path.GetFileName(archivePath)}";

                var sendConfirm = MessageBox.Show(
                    "Диагностический архив собран.\nОтправить его на электронную почту?",
                    "Отправка диагностики",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (sendConfirm != DialogResult.Yes)
                {
                    LogOperatorAction("оператор отказался от отправки диагностики на почту");
                    Process.Start(
                        "explorer.exe",
                        $"/select,\"{archivePath}\"");

                    lblStatus.Text = "Архив диагностики собран";
                    progressBar.Value = 100;
                    return;
                }

                if (!EnsureLicenseAccess(LicenseOperation.SendDiagnostics, "отправка диагностики"))
                {
                    lblStatus.Text = "Архив диагностики собран локально";
                    return;
                }

                LogOperatorAction("оператор подтвердил отправку диагностики на почту");
                await _diagnosticWorkflowService.SendArchiveAsync(archiveInfo, SetDiagnosticsProgress);

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
                LogOperatorAction($"сбор диагностики завершился ошибкой: {ex.Message}", isError: true);
                _logService.LogDebug($"Ошибка сбора диагностики: {ex.Message}");

                if (!string.IsNullOrWhiteSpace(archiveInfo?.ArchivePath) && File.Exists(archiveInfo.ArchivePath))
                {
                    Process.Start(
                        "explorer.exe",
                        $"/select,\"{archiveInfo.ArchivePath}\"");
                }

                MessageBox.Show(
                    $"Не удалось завершить диагностику:\n{ex.Message}" +
                    (archiveInfo == null ? string.Empty : $"\n\nАрхив сохранён локально:\n{archiveInfo.ArchivePath}"),
                    "Ошибка диагностики",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                lblStatus.Text = $"Ошибка диагностики: {ex.Message}";
            }
            finally
            {
                btnDiagnostics.Enabled = true;
                EndLongOperation();
            }
        }

        private void SetDiagnosticsProgress(int progress, string message)
        {
            progressBar.Value = Math.Min(Math.Max(progress, progressBar.Minimum), progressBar.Maximum);
            lblStatus.Text = message;
        }

        private void BtnMaintenance_Click(object sender, EventArgs e)
        {
            LogOperatorAction("открыто меню обслуживания точки");

            if (!HasAnyLicenseAccess(
                LicenseOperation.ReinstallComponents,
                LicenseOperation.RestoreLmDatabase))
            {
                LogOperatorAction("меню обслуживания заблокировано лицензией", isError: true);
                MessageBox.Show(
                    "В лицензии не разрешено ни одного действия обслуживания.",
                    "Функция недоступна",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            Infrastructure.Dialogs.MaintenanceAction? action = _mainFormDialogs.ShowMaintenanceAction(
                _licenseAccessPolicy.Check(LicenseOperation.ReinstallComponents).IsAllowed,
                _licenseAccessPolicy.Check(LicenseOperation.RestoreLmDatabase).IsAllowed);
            if (action == null)
            {
                LogOperatorAction("меню обслуживания точки закрыто без выбора");
                return;
            }

            switch (action.Value)
            {
                case Infrastructure.Dialogs.MaintenanceAction.ReinstallComponents:
                    LogOperatorAction("выбрано обслуживание: переустановить компоненты");
                    BtnReinstallComponents_Click(sender, e);
                    break;

                case Infrastructure.Dialogs.MaintenanceAction.RestoreLmDatabase:
                    LogOperatorAction("выбрано обслуживание: восстановить базу ЛМ ЧЗ");
                    BtnRestoreLmDatabase_Click(sender, e);
                    break;
            }
        }

        private async void BtnReinstallComponents_Click(object sender, EventArgs e)
        {
            LogOperatorAction("запрошена ручная переустановка компонентов");

            if (!EnsureNoLongOperation("ручная переустановка компонентов"))
                return;

            if (!_maintenanceWorkflow.CanModifySystem())
            {
                LogOperatorAction("ручная переустановка отменена: нет прав администратора", isError: true);
                MessageBox.Show(
                    "Для переустановки компонентов нужны права администратора.\nПерезапустите HonestFlow от имени администратора.",
                    "Нужны права администратора",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var selectedIP = await GetAuthorizedIpForManualActionAsync();
            if (selectedIP == null)
            {
                LogOperatorAction("ручная переустановка отменена: авторизация не пройдена", isError: true);
                return;
            }

            var components = _mainFormDialogs.ShowComponentSelection();
            if (components == null || components.Count == 0)
            {
                LogOperatorAction("ручная переустановка отменена: компоненты не выбраны");
                return;
            }

            string componentNames = string.Join(", ", components.Select(MainFormDialogService.GetComponentDisplayName));
            LogOperatorAction($"для ручной переустановки выбраны компоненты: {componentNames}");

            var confirm = MessageBox.Show(
                $"Будет выполнена принудительная переустановка компонентов:\n\n{componentNames}\n\nПродолжить?",
                "Ручная переустановка",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
            {
                LogOperatorAction("ручная переустановка отменена на подтверждении");
                return;
            }

            if (!TryBeginLongOperation(
                "ручная переустановка компонентов",
                LicenseOperation.ReinstallComponents))
                return;

            try
            {
                LogOperatorAction($"ручная переустановка запущена: {componentNames}");
                btnReinstallComponents.Enabled = false;
                btnMaintenance.Enabled = false;
                btnCheckWithoutPassword.Enabled = false;
                progressBar.Visible = true;
                progressBar.Value = 0;
                lblStatus.Visible = true;
                lblStatus.Text = "Ручная переустановка компонентов...";

                bool success = await _maintenanceWorkflow.ReinstallAsync(selectedIP, components);
                if (!success)
                {
                    LogOperatorAction("ручная переустановка завершена с ошибками", isError: true);
                    ShowInlineError("Ручная переустановка завершена с ошибками. Подробности записаны в журнал.", "Переустановка");
                }
                else
                {
                    LogOperatorAction("ручная переустановка завершена успешно");
                }

                await RefreshPointStatusAsync(allowDuringLongOperation: true);
            }
            finally
            {
                btnReinstallComponents.Enabled = true;
                btnMaintenance.Enabled = true;
                btnCheckWithoutPassword.Enabled = true;
                progressBar.Visible = false;
                EndLongOperation();
            }
        }

        private async void BtnRestoreLmDatabase_Click(object sender, EventArgs e)
        {
            LogOperatorAction("запрошено восстановление базы ЛМ ЧЗ");

            if (!EnsureNoLongOperation("восстановление базы ЛМ ЧЗ"))
                return;

            if (!_maintenanceWorkflow.CanModifySystem())
            {
                LogOperatorAction("восстановление базы ЛМ ЧЗ отменено: нет прав администратора", isError: true);
                MessageBox.Show(
                    "Для восстановления базы ЛМ ЧЗ нужны права администратора.\nПерезапустите HonestFlow от имени администратора.",
                    "Нужны права администратора",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var selectedIP = await GetAuthorizedIpForManualActionAsync();
            if (selectedIP == null)
            {
                LogOperatorAction("восстановление базы ЛМ ЧЗ отменено: авторизация не пройдена", isError: true);
                return;
            }

            if (!TryBeginLongOperation(
                "восстановление базы ЛМ ЧЗ",
                LicenseOperation.RestoreLmDatabase))
                return;

            try
            {
                LogOperatorAction($"восстановление базы ЛМ ЧЗ запущено для точки: {selectedIP.Name}");
                btnRestoreLmDatabase.Enabled = false;
                btnReinstallComponents.Enabled = false;
                btnMaintenance.Enabled = false;
                btnCheckWithoutPassword.Enabled = false;
                progressBar.Visible = true;
                progressBar.Value = 0;
                lblStatus.Visible = true;
                lblStatus.Text = "Восстановление базы ЛМ ЧЗ...";

                bool success = await _maintenanceWorkflow.RestoreLmDatabaseAsync(selectedIP);
                if (success)
                {
                    LogOperatorAction("восстановление базы ЛМ ЧЗ завершено успешно");
                    await RefreshPointStatusAsync(allowDuringLongOperation: true);
                }
                else
                {
                    LogOperatorAction("восстановление базы ЛМ ЧЗ завершено без успеха", isError: true);
                }
            }
            finally
            {
                btnRestoreLmDatabase.Enabled = true;
                btnReinstallComponents.Enabled = true;
                btnMaintenance.Enabled = true;
                btnCheckWithoutPassword.Enabled = true;
                progressBar.Visible = false;
                EndLongOperation();
            }
        }

        private async Task<IPData> GetAuthorizedIpForManualActionAsync()
        {
            if (_selectedIP != null)
            {
                LogOperatorAction($"используется уже авторизованная точка: {_selectedIP.Name}");
                return _selectedIP;
            }

            string enteredPassword = textBox1.Text;
            if (string.IsNullOrWhiteSpace(enteredPassword))
            {
                LogOperatorAction("ручная операция отменена: пароль точки не введен", isError: true);
                ShowInlineWarning("Введите пароль точки перед ручной переустановкой.", "Авторизация");
                textBox1.Focus();
                return null;
            }

            LicenseAuthenticationResult authentication = await AuthenticateWithLicenseAsync(enteredPassword);
            var selectedIP = authentication.Client;
            if (selectedIP == null)
            {
                LogOperatorAction("ручная операция отклонена: неверный пароль", isError: true);
                ShowInlineError("Неверный пароль. Доступ запрещён.", "Авторизация");
                textBox1.Clear();
                textBox1.Focus();
                return null;
            }

            _selectedIP = selectedIP;
            _logService.LogUser($"Пользователь для ручной операции: {selectedIP.Name}");
            return selectedIP;
        }

        private async void BtnRefreshStatus_Click(object sender, EventArgs e)
        {
            LogOperatorAction("запрошено ручное обновление статусов точки");
            bool baseStatusRefresh = sender == btnCloudAction || sender == btnRuDesktopAction;
            if (!baseStatusRefresh &&
                !EnsureLicenseAccess(LicenseOperation.ViewPointStatus, "обновление статусов"))
                return;
            await RefreshPointStatusAsync();
        }

        private async void ServiceAction_Click(object sender, EventArgs e)
        {
            if (_serviceActionRunning)
            {
                LogOperatorAction("действие со службами пропущено: уже выполняется другая операция");
                return;
            }

            if (!EnsureNoLongOperation("управление службами"))
                return;

            if (!(sender is Button button) || !(button.Tag is NodeStatus status))
                return;

            if (!status.CanManageServices)
            {
                LogOperatorAction($"открыты детали состояния: {status.ShortText}");
                _mainFormDialogs.ShowNodeDetails(status);
                return;
            }

            ServiceActionPlan plan = _pointRepairWorkflow.CreateServiceActionPlan(status);
            if (!plan.HasAdministratorAccess)
            {
                LogOperatorAction("управление службами отменено: нет прав администратора", isError: true);
                MessageBox.Show(
                    "Для управления службами нужны права администратора.\nПерезапустите HonestFlow от имени администратора.",
                    "Нужны права администратора",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            bool shouldStart = plan.ShouldStart;
            string actionName = plan.ActionName;
            string serviceList = plan.ServiceList;
            LogOperatorAction($"запрошено действие со службами: {actionName} ({serviceList})");

            if (!shouldStart)
            {
                var confirm = MessageBox.Show(
                    $"Перезапустить службы?\n\n{serviceList}",
                    "Перезапуск служб",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (confirm != DialogResult.Yes)
                {
                    LogOperatorAction($"перезапуск служб отменен на подтверждении: {serviceList}");
                    return;
                }
            }

            LicenseOperation serviceFeature = button == btnRuDesktopAction
                ? LicenseOperation.InstallRuDesktop
                : LicenseOperation.ManageServices;
            if (!TryBeginLongOperation("управление службами", serviceFeature))
                return;

            using var audit = Logger.BeginOperation("Управление службами точки", nameof(MainForm));
            try
            {
                LogOperatorAction($"операция со службами начата: {actionName} ({serviceList})");
                _serviceActionRunning = true;
                button.Enabled = false;
                btnCheckWithoutPassword.Enabled = false;
                lblStatus.Text = $"Пытаюсь {actionName} службы: {serviceList}";

                await _pointRepairWorkflow.ExecuteServiceActionAsync(plan, serviceFeature);

                lblStatus.Text = "Операция со службами завершена";
                LogOperatorAction($"операция со службами завершена: {actionName} ({serviceList})");
                await RefreshPointStatusAsync(allowDuringLongOperation: true);
            }
            catch (Exception ex)
            {
                LogOperatorAction($"операция со службами завершилась ошибкой: {ex.Message}", isError: true);
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
                EndLongOperation();
            }
        }

        private async Task RefreshPointStatusAsync(bool allowDuringLongOperation = false)
        {
            if (_selectedIP == null)
            {
                LogOperatorAction("проверка состояния точки заблокирована: продавец не авторизован", isError: true);
                lblStatus.Text = "Для проверки состояния точки войдите как продавец";
                return;
            }

            bool canViewAndRepair = _licenseAccessPolicy
                .Check(LicenseOperation.ViewPointStatus)
                .IsAllowed;

            if (!allowDuringLongOperation && IsLongOperationRunning)
            {
                LogOperatorAction($"обновление статусов пропущено: выполняется операция \"{_longOperationName}\"");
                return;
            }

            if (_statusRefreshRunning)
            {
                _logService.LogDebug("Проверка состояния точки пропущена: предыдущая проверка еще выполняется");
                return;
            }

            _statusRefreshRunning = true;
            btnCheckWithoutPassword.Enabled = false;
            SetNodeChecking(canViewAndRepair);
            lblStatus.Text = "Проверка служб и связи...";
            lblHeaderStatus.Text = "● Проверка";
            lblHeaderStatus.ForeColor = StatusYellow;

            try
            {
                PointStatusRefreshResult refresh = await _pointStatusRefreshService.RefreshAsync(
                    _selectedIP,
                    _remoteVersions,
                    canViewAndRepair,
                    _lifetimeCancellation.Token);
                PointStatusResult result = refresh.PointStatus;

                if (!allowDuringLongOperation && IsLongOperationRunning)
                {
                    _logService.LogDebug($"Результат проверки состояния точки не применен: выполняется операция \"{_longOperationName}\"");
                    return;
                }

                if (canViewAndRepair)
                {
                    ApplyNodeStatus(lblLmNode, lblLmStatusText, lblLmCircle, btnLmAction, result.Lm, "ЛМ ЧЗ");
                    ApplyNodeStatus(lblControllerNode, lblControllerStatusText, lblControllerCircle, btnControllerAction, result.Controller, "Контроллер");
                    ApplyNodeStatus(lblEsmNode, lblEsmStatusText, lblEsmCircle, btnEsmAction, result.Esm, "ЕСМ");
                    ApplyNodeStatus(lblKktNode, lblKktStatusText, lblKktCircle, btnKktAction, result.Kkt, "ККТ");
                    ApplyVersionMarkers(refresh.VersionStatuses);
                    _lastPointStatusResult = result;
                }
                else
                {
                    SetNodeLicenseRequired(lblLmNode, lblLmStatusText, lblLmCircle, btnLmAction, "ЛМ ЧЗ");
                    SetNodeLicenseRequired(lblControllerNode, lblControllerStatusText, lblControllerCircle, btnControllerAction, "Контроллер");
                    SetNodeLicenseRequired(lblEsmNode, lblEsmStatusText, lblEsmCircle, btnEsmAction, "ЕСМ");
                    SetNodeLicenseRequired(lblKktNode, lblKktStatusText, lblKktCircle, btnKktAction, "ККТ");
                    _lastPointStatusResult = null;
                }
                ApplyNodeStatus(lblCloudNode, lblCloudStatusText, lblCloudCircle, btnCloudAction, result.Cloud, "Облако");
                ApplyRuDesktopStatus(result.RuDesktop);
                _diagnosticWorkflowService.SetPointStatusReport(refresh.DiagnosticReport);
                btnPointStatusDetails.Enabled = canViewAndRepair;

                lblHeaderStatus.Text = refresh.OverallLevel == NodeLevel.Error
                    ? "● Есть проблемы"
                    : refresh.OverallLevel == NodeLevel.Warning ? "● Требует внимания" : "● Всё работает";
                lblHeaderStatus.ForeColor = refresh.OverallLevel == NodeLevel.Error
                    ? StatusRed
                    : refresh.OverallLevel == NodeLevel.Warning ? StatusYellow : StatusGreen;
                lblStatus.Text = "Проверка завершена";
                _logService.LogDebug(
                    $"Проверка состояния точки завершена: LM={result.Lm.ShortText}, Controller={result.Controller.ShortText}, ESM={result.Esm.ShortText}, KKT={result.Kkt.ShortText}, Cloud={result.Cloud.ShortText}, RuDesktop={result.RuDesktop.ShortText}");
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
                btnCheckWithoutPassword.Enabled = !IsLongOperationRunning;
                _statusRefreshRunning = false;
                ApplyLicenseAccessToUi();

                if (_statusRefreshAfterLicenseChangePending && !IsLongOperationRunning)
                {
                    _statusRefreshAfterLicenseChangePending = false;
                    _ = RefreshPointStatusAsync();
                }
            }
        }

        private void SetNodeChecking(bool canViewAndRepair)
        {
            _lastPointStatusResult = null;
            btnPointStatusDetails.Enabled = false;
            if (canViewAndRepair)
            {
                SetNodeChecking(lblLmNode, lblLmStatusText, lblLmCircle, btnLmAction, "ЛМ ЧЗ");
                SetNodeChecking(lblControllerNode, lblControllerStatusText, lblControllerCircle, btnControllerAction, "Контроллер");
                SetNodeChecking(lblEsmNode, lblEsmStatusText, lblEsmCircle, btnEsmAction, "ЕСМ");
                SetNodeChecking(lblKktNode, lblKktStatusText, lblKktCircle, btnKktAction, "ККТ");
            }
            else
            {
                SetNodeLicenseRequired(lblLmNode, lblLmStatusText, lblLmCircle, btnLmAction, "ЛМ ЧЗ");
                SetNodeLicenseRequired(lblControllerNode, lblControllerStatusText, lblControllerCircle, btnControllerAction, "Контроллер");
                SetNodeLicenseRequired(lblEsmNode, lblEsmStatusText, lblEsmCircle, btnEsmAction, "ЕСМ");
                SetNodeLicenseRequired(lblKktNode, lblKktStatusText, lblKktCircle, btnKktAction, "ККТ");
            }
            SetNodeChecking(lblCloudNode, lblCloudStatusText, lblCloudCircle, btnCloudAction, "Облако");
            SetNodeChecking(lblRuDesktopNode, lblRuDesktopStatusText, lblRuDesktopCircle, btnRuDesktopAction, "RuDesktop");
        }

        private void SetNodeLicenseRequired(
            Label nodeLabel,
            Label statusTextLabel,
            Label circle,
            Button actionButton,
            string defaultLabel)
        {
            nodeLabel.Text = defaultLabel;
            nodeLabel.ForeColor = Color.FromArgb(15, 23, 42);
            statusTextLabel.Text = "Доступно после лицензирования.";
            circle.Text = "●";
            circle.ForeColor = StatusGray;
            actionButton.Text = "Недоступно";
            actionButton.Tag = null;
            actionButton.Enabled = false;
            actionButton.Click -= ShowNodeDetails_Click;
            actionButton.Click -= ServiceAction_Click;
            actionButton.Click -= BtnRefreshStatus_Click;
            actionButton.Click -= BtnRequestHelp_Click;
            actionButton.Click -= BtnRuDesktopInstallationPending_Click;
            actionButton.Click -= RecoverLmServices_Click;
            actionButton.Click -= InitializeLm_Click;
        }

        private void SetNodeChecking(Label nodeLabel, Label statusTextLabel, Label circle, Button actionButton, string defaultLabel)
        {
            nodeLabel.Text = defaultLabel;
            nodeLabel.ForeColor = Color.FromArgb(15, 23, 42);
            statusTextLabel.Text = "Проверка...";
            SetNode(circle, actionButton, StatusGray, "Проверка");
            actionButton.Tag = null;
            actionButton.Click -= ShowNodeDetails_Click;
            actionButton.Click -= ServiceAction_Click;
            actionButton.Click -= BtnRefreshStatus_Click;
            actionButton.Click -= BtnRequestHelp_Click;
            actionButton.Click -= BtnRuDesktopInstallationPending_Click;
            actionButton.Click -= RecoverLmServices_Click;
            actionButton.Click -= InitializeLm_Click;
            actionButton.Click += BtnRefreshStatus_Click;
        }

        private void ApplyNodeStatus(Label nodeLabel, Label statusTextLabel, Label circle, Button actionButton, NodeStatus status, string defaultLabel)
        {
            Color color = status.Level switch
            {
                NodeLevel.Ok => StatusGreen,
                NodeLevel.Warning => StatusYellow,
                NodeLevel.Error => StatusRed,
                _ => StatusGray
            };

            nodeLabel.Text = defaultLabel;
            nodeLabel.ForeColor = Color.FromArgb(15, 23, 42);
            statusTextLabel.Text = string.IsNullOrWhiteSpace(status.StatusText) ? status.ShortText : status.StatusText;
            SetNode(circle, actionButton, color, status.ShortText);
            actionButton.Text = status.ActionText;
            actionButton.Tag = status;
            actionButton.Click -= BtnRefreshStatus_Click;
            actionButton.Click -= ShowNodeDetails_Click;
            actionButton.Click -= ServiceAction_Click;
            actionButton.Click -= BtnRequestHelp_Click;
            actionButton.Click -= BtnRuDesktopInstallationPending_Click;
            actionButton.Click -= RecoverLmServices_Click;
            actionButton.Click -= InitializeLm_Click;
            actionButton.Click += status.ActionKind switch
            {
                NodeActionKind.RecoverLmServices => RecoverLmServices_Click,
                NodeActionKind.InitializeLm => InitializeLm_Click,
                _ => status.CanManageServices ? ServiceAction_Click : BtnRefreshStatus_Click
            };
        }

        private async void RecoverLmServices_Click(object sender, EventArgs e)
        {
            if (!TryBeginLongOperation("восстановление служб ЛМ ЧЗ", LicenseOperation.RecoverLmServices))
                return;

            using var audit = Logger.BeginOperation("Восстановление служб ЛМ ЧЗ", nameof(MainForm));
            try
            {
                LogOperatorAction("восстановление служб ЛМ ЧЗ начато");
                await _pointRepairWorkflow.RecoverLmServicesAsync(
                    message => lblStatus.Text = message,
                    _lifetimeCancellation.Token);
                await RefreshPointStatusAsync(allowDuringLongOperation: true);
                LogOperatorAction("восстановление служб ЛМ ЧЗ завершено");
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
                LogOperatorAction("восстановление служб ЛМ ЧЗ отменено");
            }
            catch (Exception ex)
            {
                LogOperatorAction($"восстановление служб ЛМ ЧЗ завершилось ошибкой: {ex.Message}", isError: true);
                MessageBox.Show(
                    $"Не удалось восстановить службы ЛМ ЧЗ:\n{ex.Message}",
                    "ЛМ ЧЗ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                EndLongOperation();
            }
        }

        private async void InitializeLm_Click(object sender, EventArgs e)
        {
            if (_selectedIP == null || string.IsNullOrWhiteSpace(_selectedIP.Token))
            {
                MessageBox.Show(
                    "Для инициализации ЛМ ЧЗ требуется авторизованная точка с настроенным токеном.\nОбратитесь к администратору.",
                    "ЛМ ЧЗ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            DialogResult confirmation = MessageBox.Show(
                "Инициализировать ЛМ ЧЗ для выбранной точки?\n\n" +
                "Будет выполнен локальный запрос POST /api/v2/init. Токен не будет показан в журнале.",
                "Инициализация ЛМ ЧЗ",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirmation != DialogResult.Yes ||
                !TryBeginLongOperation("инициализация ЛМ ЧЗ", LicenseOperation.InitializeLm))
            {
                return;
            }

            using var audit = Logger.BeginOperation("Ручная инициализация ЛМ ЧЗ", nameof(MainForm));
            try
            {
                LogOperatorAction("ручная инициализация ЛМ ЧЗ начата");
                ApiSimpleResponse result = await _pointRepairWorkflow.InitializeLmAsync(
                    _selectedIP.Token,
                    message => lblStatus.Text = message,
                    _lifetimeCancellation.Token);
                if (!result.IsSuccess)
                {
                    LogOperatorAction(
                        $"ручная инициализация ЛМ ЧЗ отклонена API: HTTP {(int)result.StatusCode}",
                        isError: true);
                    MessageBox.Show(
                        $"ЛМ ЧЗ не удалось инициализировать: HTTP {(int)result.StatusCode}.",
                        "Инициализация ЛМ ЧЗ",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                await RefreshPointStatusAsync(allowDuringLongOperation: true);
                LogOperatorAction("ручная инициализация ЛМ ЧЗ завершена");
                MessageBox.Show(
                    "Запрос инициализации выполнен. Актуальный результат показан в строке «ЛМ ЧЗ».",
                    "Инициализация ЛМ ЧЗ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
                LogOperatorAction("ручная инициализация ЛМ ЧЗ отменена");
            }
            catch (Exception ex)
            {
                LogOperatorAction($"ручная инициализация ЛМ ЧЗ завершилась ошибкой: {ex.Message}", isError: true);
                MessageBox.Show(
                    $"Ошибка инициализации ЛМ ЧЗ:\n{ex.Message}",
                    "Инициализация ЛМ ЧЗ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                EndLongOperation();
            }
        }

        private void ApplyRuDesktopStatus(NodeStatus status)
        {
            ApplyNodeStatus(lblRuDesktopNode, lblRuDesktopStatusText, lblRuDesktopCircle, btnRuDesktopAction, status, "RuDesktop");
            btnRuDesktopAction.Click -= ShowNodeDetails_Click;
            btnRuDesktopAction.Click -= ServiceAction_Click;
            btnRuDesktopAction.Click -= BtnRefreshStatus_Click;
            btnRuDesktopAction.Click -= BtnRequestHelp_Click;
            btnRuDesktopAction.Click -= BtnRuDesktopInstallationPending_Click;

            switch (status.ActionKind)
            {
                case NodeActionKind.InstallRuDesktop:
                case NodeActionKind.ReinstallRuDesktop:
                    btnRuDesktopAction.Click += BtnRuDesktopInstallationPending_Click;
                    break;
                case NodeActionKind.ManageServices:
                    btnRuDesktopAction.Click += ServiceAction_Click;
                    break;
                case NodeActionKind.RequestRuDesktopHelp:
                    btnRuDesktopAction.Click += BtnRequestHelp_Click;
                    break;
                default:
                    btnRuDesktopAction.Click += BtnRefreshStatus_Click;
                    break;
            }
        }

        private async void BtnRuDesktopInstallationPending_Click(object sender, EventArgs e)
        {
            if (_selectedIP == null)
            {
                MessageBox.Show(
                    "Сначала войдите как продавец, чтобы HonestFlow проверил лицензию точки.",
                    "RuDesktop",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string action = btnRuDesktopAction.Tag is NodeStatus status &&
                status.ActionKind == NodeActionKind.ReinstallRuDesktop
                    ? "Переустановка"
                    : "Установка";

            RuDesktopPackage package = _ruDesktopWorkflow.GetInstallationPackage();
            DialogResult confirmation = MessageBox.Show(
                $"{action} RuDesktop {package.Version}?\n\n" +
                $"Пакет: {package.FileName}\n" +
                "Будут установлены клиент и служба RuDesktop.\n" +
                "Windows запросит разрешение администратора.",
                "RuDesktop",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirmation != DialogResult.Yes)
            {
                LogOperatorAction($"{action.ToLowerInvariant()} RuDesktop отменена пользователем");
                return;
            }

            // RuDesktop intentionally does not require the engineer password.
            if (!TryBeginLongOperation(
                    $"{action.ToLowerInvariant()} RuDesktop",
                    LicenseOperation.InstallRuDesktop))
                return;

            try
            {
                LogOperatorAction($"{action.ToLowerInvariant()} RuDesktop {package.Version} запущена");
                progressBar.Visible = true;
                progressBar.Value = 0;

                var progress = new Progress<RuDesktopInstallProgress>(value =>
                {
                    progressBar.Value = Math.Clamp(value.Percent, progressBar.Minimum, progressBar.Maximum);
                    lblStatus.Text = value.Message;
                });

                RuDesktopWorkflowInstallResult workflowResult = await _ruDesktopWorkflow.InstallAsync(
                    progress,
                    _lifetimeCancellation.Token);
                RuDesktopInstallResult result = workflowResult.InstallResult;
                if (!result.IsSuccess)
                {
                    MessageBoxIcon icon = result.Status == RuDesktopInstallStatus.UserCancelled
                        ? MessageBoxIcon.Information
                        : MessageBoxIcon.Warning;
                    MessageBox.Show(result.Message, "RuDesktop", MessageBoxButtons.OK, icon);
                    lblStatus.Text = result.Message;
                    return;
                }

                RuDesktopStatus updatedStatus = workflowResult.Status;
                await RefreshPointStatusAsync(allowDuringLongOperation: true);

                string idText = string.IsNullOrWhiteSpace(updatedStatus.Id)
                    ? "ID пока не получен"
                    : $"ID: {updatedStatus.Id}";
                string readinessText = updatedStatus.InstallationState == RuDesktopInstallationState.Ready
                    ? "Служба RuDesktop запущена."
                    : "RuDesktop установлен, но его состояние требует повторной проверки.";

                MessageBox.Show(
                    result.Message + "\n" + readinessText + "\n" + idText,
                    "RuDesktop",
                    MessageBoxButtons.OK,
                    result.Status == RuDesktopInstallStatus.RebootRequired
                        ? MessageBoxIcon.Warning
                        : MessageBoxIcon.Information);
                lblStatus.Text = result.Message;
            }
            catch (Exception ex)
            {
                LogOperatorAction($"{action.ToLowerInvariant()} RuDesktop завершилась ошибкой: {ex.Message}", isError: true);
                _logService.LogDebug($"RuDesktop UI installation error: {ex}");
                MessageBox.Show(
                    $"Не удалось установить RuDesktop:\n{ex.Message}",
                    "RuDesktop",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                lblStatus.Text = "Ошибка установки RuDesktop";
            }
            finally
            {
                EndLongOperation();
            }
        }

        private static void SetNode(Label circle, Button actionButton, Color color, string text)
        {
            circle.Text = "●";
            circle.ForeColor = color;
            actionButton.Text = text;
        }

        private void ShowNodeDetails_Click(object sender, EventArgs e)
        {
            if (!EnsureLicenseAccess(LicenseOperation.ViewPointStatus, "просмотр состояния точки"))
                return;

            if (sender is Button button && button.Tag is NodeStatus status)
            {
                LogOperatorAction($"открыты детали состояния: {status.ShortText}");
                _mainFormDialogs.ShowNodeDetails(status);
            }
        }

        private void ShowPointStatusDetails_Click(object sender, EventArgs e)
        {
            if (!EnsureLicenseAccess(LicenseOperation.ViewPointStatus, "просмотр состояния точки"))
                return;

            if (_lastPointStatusResult == null)
                return;

            LogOperatorAction("открыт общий отладочный снимок состояния точки");
            string report = _pointStatusReportBuilder.Build(_lastPointStatusResult);
            _mainFormDialogs.ShowPointStatusReport(report);
        }

        private async void BtnRequestHelp_Click(object sender, EventArgs e)
        {
            LogOperatorAction("нажата кнопка запроса помощи");

            if (!EnsureLicenseAccess(LicenseOperation.RequestHelp, "запрос помощи"))
                return;

            if (!EnsureNoLongOperation("запрос помощи"))
                return;

            if (!await EnsureRuDesktopPasswordConfiguredForHelpRequest())
                return;

            string ruDesktopId = await TryGetRuDesktopIdForHelpRequest();
            if (string.IsNullOrWhiteSpace(ruDesktopId))
            {
                MessageBox.Show(
                    "RuDesktop не выдал ID, поэтому запрос помощи сейчас отправить нельзя.\n\n" +
                    "Проверьте, что служба RuDesktop запущена, подождите немного и нажмите «Обновить».",
                    "Запрос помощи",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            Infrastructure.Dialogs.HelpRequestDialogResult helpRequest = _mainFormDialogs.ShowHelpRequest();
            if (helpRequest == null)
            {
                LogOperatorAction("запрос помощи отменен оператором");
                return;
            }

            try
            {
                btnRuDesktopAction.Enabled = false;
                lblStatus.Visible = true;
                lblStatus.Text = "Проверка состояния точки для заявки...";
                LastAuthorizedClientState lastClient = _selectedIP == null
                    ? _ruDesktopWorkflow.GetLastAuthorizedClient()
                    : null;
                LicenseObservationSnapshot licenseSnapshot = _licenseSnapshotStore.Current;
                await _helpRequestWorkflow.SendAsync(new HelpRequestWorkflowInput
                {
                    SelectedClient = _selectedIP,
                    LastClient = lastClient,
                    RuDesktopId = ruDesktopId,
                    HonestFlowVersion = System.Windows.Forms.Application.ProductVersion,
                    FiscalAddress = helpRequest.FiscalAddress,
                    ProblemType = helpRequest.ProblemType,
                    Message = helpRequest.Message,
                    LicenseSnapshot = licenseSnapshot
                },
                    _lifetimeCancellation.Token);

                ShowNotification("Заявка помощи отправлена.", "Запрос помощи", UserNotificationSeverity.Success);

                lblStatus.Text = "Заявка помощи отправлена";
            }
            catch (Exception ex)
            {
                LogOperatorAction($"не удалось отправить запрос помощи: {ex.Message}", isError: true);
                _logService.LogDebug($"Ошибка отправки запроса помощи: {ex}");
                ShowInlineError($"Не удалось отправить заявку: {ex.Message}", "Запрос помощи");
                lblStatus.Text = $"Ошибка запроса помощи: {ex.Message}";
            }
            finally
            {
                btnRuDesktopAction.Enabled = true;
                ApplyLicenseAccessToUi();
            }
        }

        private async Task<bool> EnsureRuDesktopPasswordConfiguredForHelpRequest()
        {
            try
            {
                RuDesktopHelpReadiness readiness = await _ruDesktopWorkflow.GetHelpReadinessAsync();
                if (readiness.IsReady)
                    return true;

                if (!readiness.IsInstalled)
                {
                    LogOperatorAction("запрос помощи заблокирован: RuDesktop не найден на этом компьютере", isError: true);
                    MessageBox.Show(
                        "RuDesktop не найден на этом компьютере.\n\n" +
                        "Запрос помощи доступен после настройки RuDesktop.",
                        "Запрос помощи",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return false;
                }

                LogOperatorAction("запрос помощи: требуется настройка постоянного пароля RuDesktop");
                IPData selectedClient = await ResolveClientForRuDesktopSetupAsync();
                if (selectedClient == null)
                    return false;

                await ConfigureRuDesktopPasswordFromClient(selectedClient);
                readiness = await _ruDesktopWorkflow.GetHelpReadinessAsync();
                return readiness.IsReady;
            }
            catch (Exception ex)
            {
                LogOperatorAction($"запрос помощи заблокирован: не удалось проверить RuDesktop ({ex.Message})", isError: true);
                MessageBox.Show(
                    $"Не удалось проверить состояние RuDesktop:\n{ex.Message}",
                    "Запрос помощи",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return false;
        }

        private async Task<IPData> ResolveClientForRuDesktopSetupAsync()
        {
            if (_selectedIP != null)
                return _selectedIP;

            string enteredPassword = _mainFormDialogs.ShowStartupRuDesktopPassword();
            if (string.IsNullOrWhiteSpace(enteredPassword))
            {
                LogOperatorAction("запрос помощи: настройка RuDesktop отменена, пароль точки не введен");
                return null;
            }

            LicenseAuthenticationResult authentication = await AuthenticateWithLicenseAsync(enteredPassword);
            IPData selectedClient = authentication.Client;
            if (selectedClient == null)
            {
                LogOperatorAction("запрос помощи: настройка RuDesktop отклонена, неверный пароль точки", isError: true);
                ShowInlineWarning("Неверный пароль точки. Запрос помощи не отправлен.", "Запрос помощи");
                return null;
            }

            ApplyAuthorizedClient(selectedClient);

            if (!selectedClient.RuDesktop.Enabled || string.IsNullOrWhiteSpace(selectedClient.RuDesktop.Password))
            {
                LogOperatorAction("запрос помощи: настройка RuDesktop невозможна, в карточке клиента нет пароля RuDesktop", isError: true);
                MessageBox.Show(
                    "В карточке клиента не указан пароль RuDesktop.\nЗапрос помощи не отправлен.",
                    "Запрос помощи",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return null;
            }

            return selectedClient;
        }

        private async Task<string> TryGetRuDesktopIdForHelpRequest()
        {
            string lastKnownId = await _ruDesktopWorkflow.ResolveHelpIdAsync();
            LogOperatorAction(
                string.IsNullOrWhiteSpace(lastKnownId)
                    ? "RuDesktop ID для заявки помощи не получен, заявка будет отправлена без ID"
                    : "RuDesktop ID для заявки помощи взят из последнего сохраненного состояния",
                isError: string.IsNullOrWhiteSpace(lastKnownId));

            return lastKnownId;
        }

        private void ApplyVersionMarkers(ComponentVersionStatus[] statuses)
        {
            Label[] labels = { lblLmNode, lblKktNode, lblEsmNode, lblControllerNode };
            string[] names = { "ЛМ ЧЗ", "ККТ", "ЕСМ", "Контроллер" };
            for (int index = 0; index < labels.Length; index++)
            {
                if (statuses == null || index >= statuses.Length)
                    continue;

                ComponentVersionStatus status = statuses[index];
                bool current = status.State == ComponentVersionState.Current;
                labels[index].Text = $"{(current ? "✓" : "✕")} {names[index]}";
                labels[index].ForeColor = current ? StatusGreen : StatusRed;
                _licenseToolTip.SetToolTip(labels[index], BuildVersionTooltip(status));
            }
        }

        private static string BuildVersionTooltip(ComponentVersionStatus status)
        {
            string installed = status.InstalledVersion ?? "не установлен";
            string expected = status.ExpectedVersion ?? "не загружена";
            return $"Установленная версия: {installed}\nТребуемая версия: {expected}\n{status.StateText}";
        }

        private void BtnDetails_Click(object sender, EventArgs e)
        {
            LogOperatorAction("открытие журнала выполнения");

            if (!EnsureLicenseAccess(LicenseOperation.ViewPointStatus, "открытие журнала"))
                return;

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
                    LogOperatorAction($"журнал выполнения открыт: {logPath}");
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
                    LogOperatorAction($"папка логов открыта: {logsFolder}");
                    return;
                }

                LogOperatorAction("журнал выполнения не найден", isError: true);
                ShowInlineWarning("Файл журнала не найден.", "Журнал выполнения");
            }
            catch (Exception ex)
            {
                LogOperatorAction($"не удалось открыть журнал выполнения: {ex.Message}", isError: true);
                _logService.LogDebug($"Ошибка открытия журнала выполнения: {ex}");
                MessageBox.Show(
                    $"Не удалось открыть журнал:\n{ex.Message}",
                    "Журнал выполнения",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private bool IsLongOperationRunning => !string.IsNullOrWhiteSpace(_longOperationName);

        private void ApplyAuthorizedClient(IPData selectedIP)
        {
            _selectedIP = selectedIP;
            textBox1.Clear();
            textBox1.Enabled = false;
            button2.Enabled = false;
            button2.Visible = false;
            btnRefreshLicense.Visible = true;
            leftLayout.RowStyles[1].Height = 40;
            leftLayout.RowStyles[2].Height = 40;
            leftLayout.RowStyles[3].Height = 40;
            btnStartInstallation.Text = "Запустить установку";
            btnStartInstallation.Visible = true;
            btnMaintenance.Visible = true;
            leftLayout.RowStyles[9].Height = 40;
            leftLayout.RowStyles[10].Height = 0;
            btnRateApplication.Visible = true;
            btnRateApplication.Enabled = true;
            lblRatingThanks.Visible = false;
            btnRefreshLicense.Enabled = true;
            btnMaintenance.Text = "Обслуживание точки";
            btnDetails.Visible = true;
            lblStatus.Text = "Лицензия проверена. Активен режим продавца.";
            lblAuthorizedClient.Text = "Авторизован: " + selectedIP.Name;

            _logService.LogUser($"Пользователь: {selectedIP.Name}");
            _logService.LogDebug($"Авторизован: {selectedIP.Name}, ИНН: {selectedIP.Inn}, Разрядность: {selectedIP.Architecture}");
            _ruDesktopWorkflow.SaveLastAuthorizedClient(selectedIP);
            ApplyAuthorizedUiMode();
            ApplyLicenseAccessToUi();
            HandleLicenseSnapshot(_licenseSnapshotStore.Current);
        }

        private async Task OfferStartupRuDesktopSetupIfNeeded()
        {
            if (_selectedIP != null || IsLongOperationRunning)
                return;

            bool needsSetup = await _ruDesktopWorkflow.NeedsInitialPasswordSetupAsync();
            if (!needsSetup)
                return;

            LogOperatorAction("RuDesktop: требуется первичная настройка постоянного пароля");

            string enteredPassword = _mainFormDialogs.ShowStartupRuDesktopPassword();
            if (string.IsNullOrWhiteSpace(enteredPassword))
            {
                LogOperatorAction("RuDesktop: первичная настройка пропущена, пароль точки не введен");
                return;
            }

            LicenseAuthenticationResult authentication = await AuthenticateWithLicenseAsync(enteredPassword);
            var selectedIP = authentication.Client;
            if (selectedIP == null)
            {
                LogOperatorAction("RuDesktop: первичная настройка отклонена, неверный пароль точки", isError: true);
                ShowInlineWarning("Неверный пароль точки. RuDesktop не настроен.", "RuDesktop");
                return;
            }

            ApplyAuthorizedClient(selectedIP);

            if (!selectedIP.RuDesktop.Enabled || string.IsNullOrWhiteSpace(selectedIP.RuDesktop.Password))
            {
                LogOperatorAction("RuDesktop: первичная настройка отменена, в карточке клиента нет пароля RuDesktop", isError: true);
                MessageBox.Show(
                    "В карточке клиента не указан пароль RuDesktop.\nНастройка пропущена.",
                    "Настройка RuDesktop",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            await ConfigureRuDesktopPasswordFromClient(selectedIP);
        }

        private async Task ConfigureRuDesktopPasswordFromClient(IPData selectedIP)
        {
            if (!TryBeginLongOperation("настройка RuDesktop", LicenseOperation.ConfigureRuDesktop))
                return;

            try
            {
                LogOperatorAction($"RuDesktop: запуск настройки постоянного пароля для клиента {selectedIP.Name}");
                lblStatus.Visible = true;
                lblStatus.Text = "Настройка RuDesktop...";

                RuDesktopSetupResult result = await _ruDesktopWorkflow.ConfigurePasswordAsync(selectedIP);
                if (!result.Success)
                {
                    LogOperatorAction($"RuDesktop: не удалось создать постоянный пароль: {result.ErrorMessage}", isError: true);
                    MessageBox.Show(
                        $"Не удалось настроить постоянный пароль RuDesktop:\n{result.ErrorMessage}",
                        "RuDesktop",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                MessageBox.Show(
                    "Постоянный пароль RuDesktop настроен.\n\n" +
                    $"ID: {result.Id}\n" +
                    "Пароль: из карточки клиента\n\n" +
                    "В лог HonestFlow пароль не записан.",
                    "RuDesktop",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                lblStatus.Text = "RuDesktop настроен";
            }
            finally
            {
                EndLongOperation();
            }
        }

        private async Task OfferRuDesktopPasswordSetupIfNeeded(IPData selectedIP)
        {
            if (!_ruDesktopWorkflow.ShouldOfferPasswordSetup(selectedIP))
                return;

            string ruDesktopId = await _ruDesktopWorkflow.GetIdAsync();
            if (string.IsNullOrWhiteSpace(ruDesktopId))
            {
                _logService.LogDebug("RuDesktop: предложение настройки пропущено, ID не получен");
                return;
            }

            LogOperatorAction($"RuDesktop: предложена настройка постоянного пароля, ID: {ruDesktopId}");

            var answer = MessageBox.Show(
                "Обнаружен RuDesktop.\n\n" +
                $"ID для подключения: {ruDesktopId}\n\n" +
                "Можно применить постоянный пароль из карточки клиента.\n" +
                "Пароль не будет записан в лог HonestFlow.\n\n" +
                "Настроить пароль сейчас?\n\n" +
                "Да — настроить пароль\n" +
                "Нет — спросить позже\n" +
                "Отмена — закрыть окно",
                "Настройка RuDesktop",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (answer == DialogResult.Cancel)
            {
                LogOperatorAction("RuDesktop: оператор закрыл окно настройки постоянного пароля");
                return;
            }

            if (answer != DialogResult.Yes)
            {
                LogOperatorAction("RuDesktop: оператор отложил настройку постоянного пароля");
                return;
            }

            await ConfigureRuDesktopPasswordFromClient(selectedIP);
        }

        private bool EnsureNoLongOperation(string requestedAction)
        {
            if (!IsLongOperationRunning)
                return true;

            string message = $"Сейчас выполняется операция: {_longOperationName}. Дождитесь завершения.";
            LogOperatorAction($"{requestedAction} не запущено: {message}");
            ShowNotification(message, "Операция уже выполняется", UserNotificationSeverity.Information);
            return false;
        }

        private bool TryBeginLongOperation(
            string operationName,
            LicenseOperation? requiredFeature = null)
        {
            if (requiredFeature.HasValue && !EnsureLicenseAccess(requiredFeature.Value, operationName))
                return false;

            if (!EnsureNoLongOperation(operationName))
                return false;

            _longOperationName = operationName;
            SetLongOperationControlsEnabled(false);
            return true;
        }

        private void EndLongOperation()
        {
            _longOperationName = null;
            SetLongOperationControlsEnabled(true);

            if (_statusRefreshAfterLicenseChangePending && !_statusRefreshRunning)
            {
                _statusRefreshAfterLicenseChangePending = false;
                _ = RefreshPointStatusAsync();
            }
        }

        private void SetLongOperationControlsEnabled(bool enabled)
        {
            bool authEnabled = enabled && _selectedIP == null;

            textBox1.Enabled = authEnabled;
            button2.Enabled = authEnabled;
            btnStartInstallation.Enabled = enabled;
            btnCheckWithoutPassword.Enabled = enabled;
            btnDiagnostics.Enabled = enabled;
            btnMaintenance.Enabled = enabled;
            btnReinstallComponents.Enabled = enabled;
            btnRestoreLmDatabase.Enabled = enabled;
            btnOpenKktDriver.Enabled = enabled;
            btnOpenEsm.Enabled = enabled;

            SetNodeActionButtonsEnabled(enabled);

            if (enabled)
                ApplyLicenseAccessToUi();
        }

        private void SetNodeActionButtonsEnabled(bool enabled)
        {
            btnLmAction.Enabled = enabled;
            btnControllerAction.Enabled = enabled;
            btnPointStatusDetails.Enabled = enabled && _lastPointStatusResult != null;
            btnEsmAction.Enabled = enabled;
            btnKktAction.Enabled = enabled;
            btnCloudAction.Enabled = enabled;
            btnRuDesktopAction.Enabled = enabled;
        }

        private bool HasAnyLicenseAccess(params LicenseOperation[] operations)
        {
            return operations != null && operations.Any(operation =>
                _licenseAccessPolicy.Check(operation).IsAllowed);
        }

        private bool EnsureLicenseAccess(LicenseOperation operation, string operationName)
        {
            LicenseAccessResult access = _licenseAccessPolicy.Check(operation);
            if (access.IsAllowed)
                return true;

            Logger.Warning(
                $"Event=LicenseOperationDenied Operation={operation} TechnicalCode={access.TechnicalCode}",
                nameof(MainForm));
            LogOperatorAction($"{operationName} заблокировано лицензией (код: {access.TechnicalCode})", isError: true);
            ShowInlineWarning(access.Message, "Функция недоступна");
            return false;
        }

        private void ApplyLicenseAccessToUi()
        {
            if (IsDisposed || IsLongOperationRunning)
                return;

            SetFeatureAvailability(btnDiagnostics, LicenseOperation.CollectDiagnostics);
            SetFeatureAvailability(btnCheckWithoutPassword, LicenseOperation.ViewPointStatus);
            SetFeatureAvailability(btnDetails, LicenseOperation.ViewPointStatus);
            SetAnyFeatureAvailability(
                btnMaintenance,
                LicenseOperation.ReinstallComponents,
                LicenseOperation.RestoreLmDatabase);
            SetFeatureAvailability(btnReinstallComponents, LicenseOperation.ReinstallComponents);
            SetFeatureAvailability(btnRestoreLmDatabase, LicenseOperation.RestoreLmDatabase);
            SetFeatureAvailability(btnOpenKktDriver, LicenseOperation.OpenLocalTools);
            SetFeatureAvailability(btnOpenEsm, LicenseOperation.OpenLocalTools);

            if (_selectedIP != null)
                SetFeatureAvailability(btnStartInstallation, LicenseOperation.InstallComponents);

            ApplyNodeLicenseAccess(btnLmAction);
            ApplyNodeLicenseAccess(btnControllerAction);
            SetFeatureAvailability(btnPointStatusDetails, LicenseOperation.ViewPointStatus);
            ApplyNodeLicenseAccess(btnEsmAction);
            ApplyNodeLicenseAccess(btnKktAction);
            SetFeatureAvailability(btnCloudAction, LicenseOperation.CollectDiagnostics);
            ApplyRuDesktopLicenseAccess();

            if (_selectedIP == null)
                ApplyDiagnosticsOnlyUiMode();
        }

        private void ApplyDiagnosticsOnlyUiMode()
        {
            btnCheckWithoutPassword.Text = "Запросить помощь";
            btnCheckWithoutPassword.Click -= BtnRefreshStatus_Click;
            btnCheckWithoutPassword.Click -= BtnRequestHelp_Click;
            btnCheckWithoutPassword.Click += BtnRequestHelp_Click;
            btnCheckWithoutPassword.Enabled = _licenseAccessPolicy
                .Check(LicenseOperation.RequestHelp)
                .IsAllowed;

            btnDiagnostics.Visible = true;
            btnOpenKktDriver.Visible = false;
            btnOpenEsm.Visible = false;
            btnDetails.Visible = false;
            leftLayout.RowStyles[6].Height = 0;
            leftLayout.RowStyles[7].Height = 0;
            leftLayout.RowStyles[8].Height = 0;

            panelNodes.Enabled = false;
            btnPointStatusDetails.Enabled = false;
            SetNodeActionButtonsEnabled(false);
            lblHeaderStatus.Text = "● Требуется авторизация";
            lblAuthorizedClient.Text = "Продавец не авторизован";
            lblStatus.Text = "Доступны сбор диагностики и запрос помощи";

            foreach (Label statusLabel in new[]
            {
                lblLmStatusText,
                lblControllerStatusText,
                lblEsmStatusText,
                lblKktStatusText,
                lblCloudStatusText,
                lblRuDesktopStatusText
            })
            {
                statusLabel.Text = "Войдите как продавец";
            }
        }

        private void ApplyAuthorizedUiMode()
        {
            btnCheckWithoutPassword.Text = "Обновить статусы";
            btnCheckWithoutPassword.Click -= BtnRequestHelp_Click;
            btnCheckWithoutPassword.Click -= BtnRefreshStatus_Click;
            btnCheckWithoutPassword.Click += BtnRefreshStatus_Click;

            btnOpenKktDriver.Visible = true;
            btnOpenEsm.Visible = true;
            btnDetails.Visible = true;
            leftLayout.RowStyles[6].Height = 40;
            leftLayout.RowStyles[7].Height = 40;
            leftLayout.RowStyles[8].Height = 40;
            panelNodes.Enabled = true;
        }

        private void ApplyRuDesktopLicenseAccess()
        {
            LicenseOperation operation = btnRuDesktopAction.Tag is NodeStatus status
                ? status.ActionKind switch
                {
                    NodeActionKind.InstallRuDesktop => LicenseOperation.InstallRuDesktop,
                    NodeActionKind.ReinstallRuDesktop => LicenseOperation.InstallRuDesktop,
                    NodeActionKind.ManageServices => LicenseOperation.InstallRuDesktop,
                    NodeActionKind.RequestRuDesktopHelp => LicenseOperation.RequestHelp,
                    _ => LicenseOperation.InstallRuDesktop
                }
                : LicenseOperation.InstallRuDesktop;

            SetFeatureAvailability(btnRuDesktopAction, operation);
        }

        private void ApplyNodeLicenseAccess(Button button)
        {
            LicenseOperation operation = button.Tag is NodeStatus status
                ? status.ActionKind switch
                {
                    NodeActionKind.RecoverLmServices => LicenseOperation.RecoverLmServices,
                    NodeActionKind.InitializeLm => LicenseOperation.InitializeLm,
                    _ when status.CanManageServices => LicenseOperation.ManageServices,
                    _ => LicenseOperation.ViewPointStatus
                }
                : LicenseOperation.ViewPointStatus;
            SetFeatureAvailability(button, operation);
        }

        private void SetAnyFeatureAvailability(
            Control control,
            params LicenseOperation[] operations)
        {
            bool isAllowed = HasAnyLicenseAccess(operations);
            control.Enabled = isAllowed;
            _licenseToolTip.SetToolTip(
                control,
                isAllowed ? string.Empty : "В лицензии не разрешены действия этого раздела.");
        }

        private void SetFeatureAvailability(Control control, LicenseOperation operation)
        {
            LicenseAccessResult access = _licenseAccessPolicy.Check(operation);
            control.Enabled = access.IsAllowed;
            _licenseToolTip.SetToolTip(control, access.IsAllowed ? string.Empty : access.Message);
        }

        private async void LicenseSnapshotChanged(LicenseObservationSnapshot snapshot)
        {
            if (IsDisposed || Disposing)
                return;

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => LicenseSnapshotChanged(snapshot)));
                    return;
                }

                ApplyLicenseAccessToUi();
                HandleLicenseSnapshot(snapshot);

                if (!_licensePresentationService.IsForClient(_selectedIP, snapshot))
                    return;

                if (_statusRefreshRunning || IsLongOperationRunning)
                {
                    _statusRefreshAfterLicenseChangePending = true;
                    return;
                }

                await RefreshPointStatusAsync();
            }
            catch (InvalidOperationException)
            {
                // Окно уже закрывается; решение сохранено в snapshot store и в журнале.
            }
        }

        private async void HandleLicenseSnapshot(LicenseObservationSnapshot snapshot)
        {
            PointAddressResult resolvedAddress = _mainFormDialogs.ResolvePointAddress(snapshot);
            DeviceRegistrationAction registrationAction = await _deviceRegistrationWorkflow.EvaluateAsync(
                snapshot,
                _selectedIP,
                CancellationToken.None);
            if (registrationAction == DeviceRegistrationAction.AlreadySent)
            {
                lblStatus.Text = "Заявка на регистрацию этого компьютера уже отправлена.";
                PresentLicenseDecision(snapshot);
                return;
            }

            if (registrationAction == DeviceRegistrationAction.RequestRegistration)
            {
                DialogResult confirmation = MessageBox.Show(
                    this,
                    $"Компьютер не зарегистрирован для точки «{_selectedIP.Name}».\n\n" +
                    "Отправить заявку на регистрацию?",
                    "Регистрация устройства",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (confirmation != DialogResult.Yes)
                {
                    PresentLicenseDecision(snapshot);
                    return;
                }
            }

            if (registrationAction is DeviceRegistrationAction.RequestRegistration or DeviceRegistrationAction.SynchronizeAddress)
            {
                bool needsRegistration = registrationAction == DeviceRegistrationAction.RequestRegistration;
                string pointAddress = resolvedAddress.Address ?? _mainFormDialogs.ResolvePointAddressForOnlineAction(
                    needsRegistration ? "Заявка на лицензию" : "Адрес торговой точки",
                    needsRegistration
                        ? "Для отправки заявки укажите адрес торговой точки."
                        : "В лицензии этого устройства нет адреса. Укажите адрес торговой точки для его добавления.");
                await SendDeviceRegistrationRequestSafelyAsync(snapshot, pointAddress);
            }

            PresentLicenseDecision(snapshot);
        }

        private void PresentLicenseDecision(LicenseObservationSnapshot snapshot)
        {
            if (snapshot == null ||
                snapshot.EnforcementMode != LicenseEnforcementMode.Enforced ||
                _selectedIP == null ||
                ReferenceEquals(snapshot, _lastPresentedLicenseSnapshot))
            {
                return;
            }

            _lastPresentedLicenseSnapshot = snapshot;
            LicenseDecisionPresentation presentation = _licensePresentationService.Create(snapshot);
            if (presentation.IsWarning)
                ShowLicenseWarning(presentation.Title, presentation.Message);
            else
                lblStatus.Text = presentation.Message;
        }


        private async Task SendDeviceRegistrationRequestSafelyAsync(
            LicenseObservationSnapshot snapshot,
            string pointAddress)
        {
            DeviceRegistrationDeliveryStatus status = await _deviceRegistrationWorkflow.SendAsync(
                snapshot,
                pointAddress,
                GetHonestFlowVersion(),
                CancellationToken.None);

            if (IsDisposed || Disposing || status != DeviceRegistrationDeliveryStatus.Sent)
                return;

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => lblStatus.Text =
                        "Заявка регистрации устройства автоматически отправлена."));
                }
                else
                {
                    lblStatus.Text = "Заявка регистрации устройства автоматически отправлена.";
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void ShowLicenseWarning(string title, string message)
        {
            lblStatus.Visible = true;
            lblStatus.Text = message;
            ShowInlineWarning(message, title);
        }

        private static string GetHonestFlowVersion() =>
            typeof(MainForm).Assembly.GetName().Version?.ToString() ?? "unknown";

        private void LogOperatorAction(string action, bool isError = false)
        {
            _logService.LogUser($"Оператор: {action}", isError);
        }

    }
}
