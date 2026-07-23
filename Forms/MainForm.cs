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
using HonestFlow.Application.Security;
using HonestFlow.Application.Ui;
using HonestFlow.Infrastructure.Licensing;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Models.Licensing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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
        private bool _useRemoteConfigMode = false;
        private List<IPData> _remoteIps;
        private VersionsData _remoteVersions;
        private IPData _selectedIP;

        private readonly DiagnosticArchiveService _diagnosticArchiveService;
        private readonly DiagnosticsEmailSender _diagnosticsEmailSender;
        private readonly LmDatabaseRestoreService _lmDatabaseRestoreService;
        private readonly RuDesktopService _ruDesktopService;
        private readonly IRuDesktopInstaller _ruDesktopInstaller;
        private readonly HelpRequestEmailSender _helpRequestEmailSender;
        private readonly AppRatingEmailSender _appRatingEmailSender;
        private readonly WindowsServiceControlService _serviceControlService;
        private readonly ExternalApplicationLauncher _externalApplicationLauncher;
        private readonly WindowIconService _windowIconService;
        private readonly IUserDialogService _dialogService;
        private PointStatusService _pointStatusService;
        private PointStatusResult _lastPointStatusResult;
        private bool _statusRefreshRunning;
        private bool _serviceActionRunning;
        private string _longOperationName;
        private LicenseObservationSnapshot _lastPresentedLicenseSnapshot;
        private readonly ILicenseObservationSnapshotStore _licenseSnapshotStore;
        private readonly ILicenseAccessPolicy _licenseAccessPolicy;
        private readonly ToolTip _licenseToolTip = new();
        private readonly DeviceRegistrationRequestService _deviceRegistrationRequestService = new();
        private readonly DeviceRegistrationCoordinator _deviceRegistrationCoordinator;
        private readonly IPointAddressService _pointAddressService;
        private readonly IEngineerAccessService _engineerAccessService;
        private readonly IPData _startupAuthorizedClient;
        private readonly bool _startupAuthenticationHandled;
        private readonly CancellationTokenSource _lifetimeCancellation = new();

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

            _logService = new LogService();
            _progressService = new ProgressService(progressBar, lblStatus);
            _dialogService = new WinFormsDialogService(this);
            _ruDesktopService = new RuDesktopService(_logService);
            _ruDesktopInstaller = new RuDesktopInstaller(_logService);
            _diagnosticArchiveService = new DiagnosticArchiveService(_logService);
            _diagnosticsEmailSender = new DiagnosticsEmailSender(_logService, _ruDesktopService);
            _helpRequestEmailSender = new HelpRequestEmailSender(_logService);
            _appRatingEmailSender = new AppRatingEmailSender(_logService);
            _pointAddressService = new PointAddressService(_logService);
            _serviceControlService = new WindowsServiceControlService();
            _externalApplicationLauncher = new ExternalApplicationLauncher();
            _windowIconService = new WindowIconService(_logService);
            _deviceRegistrationCoordinator = new DeviceRegistrationCoordinator(
                _deviceRegistrationRequestService,
                new SmtpDeviceRegistrationRequestSender(),
                new DpapiDeviceRegistrationDeliveryStateStore());
            _engineerAccessService = new EngineerAccessService();
            _licenseSnapshotStore = LicenseObservationSnapshotStore.Instance;
            LicenseEnforcementMode licenseMode = LicenseRuntimeConfiguration.FromEnvironment().EnforcementMode;
            _licenseAccessPolicy = new LicenseAccessPolicy(licenseMode, _licenseSnapshotStore);
            _licenseSnapshotStore.SnapshotChanged += LicenseSnapshotChanged;
            FormClosed += (_, _) =>
            {
                _lifetimeCancellation.Cancel();
                _licenseSnapshotStore.SnapshotChanged -= LicenseSnapshotChanged;
            };

            startup ??= new ApplicationStartupService(_logService, _progressService, _dialogService).Start();
            _useRemoteConfigMode = startup.UseRemoteConfigMode;
            _remoteIps = startup.Ips ?? startup.RemoteIps;
            _remoteVersions = startup.RemoteVersions;
            _authService = startup.AuthService;
            _startupAuthorizedClient = startup.AuthorizedClient;
            _startupAuthenticationHandled = startup.SellerAuthenticationHandled;
            _installationService = new InstallationService(_logService, _progressService, _dialogService, _useRemoteConfigMode);
            _lmDatabaseRestoreService = new LmDatabaseRestoreService(_logService, _progressService, _dialogService, _useRemoteConfigMode);
            _pointStatusService = new PointStatusService(_useRemoteConfigMode, _remoteIps?.Count ?? 0, _remoteIps, _ruDesktopService);

            InitializeUiState();
            WireUiEvents();
            ApplyLicenseAccessToUi();
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
            SetNodeChecking();
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
                ApplyAuthorizedClient(_startupAuthorizedClient);
            else if (!_startupAuthenticationHandled)
                await PromptSellerLoginAsync();

            await RefreshPointStatusAsync();
        }

        private async void BtnRefreshLicense_Click(object sender, EventArgs e)
        {
            if (_selectedIP == null)
            {
                MessageBox.Show(
                    "Сначала войдите как продавец.",
                    "Обновление лицензии",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (_authService is not ILicenseObservationRefresher refresher)
            {
                MessageBox.Show(
                    "Повторная проверка лицензии недоступна в текущем режиме.",
                    "Обновление лицензии",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
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

                LicenseObservationSnapshot snapshot = await refresher.RefreshLicenseAsync(
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
                MessageBox.Show(
                    "Не удалось обновить лицензию. Проверьте подключение к интернету и повторите попытку.",
                    "Обновление лицензии",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
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
                MessageBox.Show(
                    "Сначала войдите как продавец.",
                    "Оценить HonestFlow",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                btnRateApplication.Enabled = false;
                lblStatus.Text = "Отправка оценки HonestFlow...";

                string pointAddress = ResolveCurrentPointAddress().Address;
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
                MessageBox.Show(
                    $"Не удалось отправить оценку:\n{ex.Message}",
                    "Оценить HonestFlow",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
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

            if (!EnsureLicenseAccess(LicenseFeature.ManualTools, $"открытие {title}"))
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

                MessageBox.Show(
                    $"Не найден файл:\n{ex.FileName}",
                    title,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                LogOperatorAction($"не удалось открыть {title}: {ex.Message}", isError: true);
                _logService.LogDebug($"Ошибка запуска {title}: {ex}");

                MessageBox.Show(
                    $"Не удалось открыть {title}:\n{ex.Message}",
                    title,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
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
                MessageBox.Show("Неверный пароль!\nДоступ запрещен.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            ApplyAuthorizedClient(selectedIP);
        }

        private async void BtnStartInstallation_Click(object sender, EventArgs e)
        {
            await StartInstallationForAuthorizedUser();
        }

        private async Task<LicenseAuthenticationResult> AuthenticateWithLicenseAsync(string password)
        {
            if (_authService is not ILicenseAuthenticatingAuthService licenseAuth)
            {
                IPData client = _authService.Authenticate(password);
                return new LicenseAuthenticationResult(client, _licenseSnapshotStore.Current);
            }

            using var progressForm = new LicenseCheckProgressForm();
            var progress = new Progress<LicenseAuthenticationProgress>(progressForm.Report);
            progressForm.Show(this);
            progressForm.BringToFront();

            try
            {
                LicenseAuthenticationResult result = await licenseAuth.AuthenticateAsync(
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
            if (selectedIP == null)
            {
                LogOperatorAction("запуск проверки отменен: пользователь не авторизован", isError: true);
                MessageBox.Show("Сначала выполните вход.", "Авторизация", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Utils.IsAdministrator())
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

            if (!TryBeginLongOperation(
                "проверка и установка компонентов",
                LicenseFeature.Install,
                requiresEngineerAccess: true))
                return;

            progressBar.Visible = true;
            lblStatus.Visible = true;

            try
            {
                bool success = await _installationService.CheckLmAndInstall(selectedIP);
                if (!success)
                {
                    LogOperatorAction("проверка и установка завершены с ошибкой", isError: true);
                    MessageBox.Show("Установка не выполнена. Смотрите лог.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    LogOperatorAction("проверка и установка завершены успешно");
                }

                progressBar.Visible = false;
                lblStatus.Visible = false;

                await RefreshPointStatusAsync(allowDuringLongOperation: true);
            }
            finally
            {
                progressBar.Visible = false;
                EndLongOperation();
            }
        }

        private async void BtnDiagnostics_Click(object sender, EventArgs e)
        {
            LogOperatorAction("нажата кнопка сбора диагностики");

            if (!TryBeginLongOperation("сбор диагностики", LicenseFeature.Diagnostics))
                return;

            DiagnosticArchiveInfo archiveInfo = null;

            try
            {
                DiagnosticLogSelection selection = ShowDiagnosticLogSelectionDialog();
                if (selection == null)
                {
                    LogOperatorAction("сбор диагностики отменен на выборе логов");
                    return;
                }

                LogOperatorAction($"запущен сбор диагностики: {DescribeDiagnosticSelection(selection)}");
                btnDiagnostics.Enabled = false;
                progressBar.Visible = true;
                progressBar.Value = 0;
                lblStatus.Text = "Сборка архива диагностики...";

                string pointAddress = ResolveCurrentPointAddress().Address;
                archiveInfo = await Task.Run(() =>
                    _diagnosticArchiveService.CreateArchiveInfo(selection, pointAddress));
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

                if (!EnsureLicenseAccess(LicenseFeature.SendLogs, "отправка диагностики"))
                {
                    lblStatus.Text = "Архив диагностики собран локально";
                    return;
                }

                LogOperatorAction("оператор подтвердил отправку диагностики на почту");
                await _diagnosticsEmailSender.SendWithRetries(archivePath, SetDiagnosticsProgress, archiveInfo.FiscalAddress);

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

            if (!EnsureLicenseAccess(LicenseFeature.Repair, "открытие меню обслуживания"))
                return;

            if (!EnsureEngineerAccess("открытие меню обслуживания"))
                return;

            var action = ShowMaintenanceActionDialog();
            if (action == null)
            {
                LogOperatorAction("меню обслуживания точки закрыто без выбора");
                return;
            }

            switch (action.Value)
            {
                case MaintenanceAction.ReinstallComponents:
                    LogOperatorAction("выбрано обслуживание: переустановить компоненты");
                    BtnReinstallComponents_Click(sender, e);
                    break;

                case MaintenanceAction.RestoreLmDatabase:
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

            if (!Utils.IsAdministrator())
            {
                LogOperatorAction("ручная переустановка отменена: нет прав администратора", isError: true);
                MessageBox.Show(
                    "Для переустановки компонентов нужны права администратора.\nПерезапустите HonestFlow от имени администратора.",
                    "Нужны права администратора",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var selectedIP = GetAuthorizedIpForManualAction();
            if (selectedIP == null)
            {
                LogOperatorAction("ручная переустановка отменена: авторизация не пройдена", isError: true);
                return;
            }

            if (!EnsureEngineerAccess("ручная переустановка компонентов"))
                return;

            var components = ShowComponentSelectionDialog();
            if (components == null || components.Count == 0)
            {
                LogOperatorAction("ручная переустановка отменена: компоненты не выбраны");
                return;
            }

            string componentNames = string.Join(", ", components.Select(GetComponentDisplayName));
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
                LicenseFeature.Repair,
                requiresEngineerAccess: true))
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

                bool success = await _installationService.ReinstallSelectedComponents(selectedIP, components);
                if (!success)
                {
                    LogOperatorAction("ручная переустановка завершена с ошибками", isError: true);
                    MessageBox.Show("Ручная переустановка завершена с ошибками. Смотрите лог.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

            if (!Utils.IsAdministrator())
            {
                LogOperatorAction("восстановление базы ЛМ ЧЗ отменено: нет прав администратора", isError: true);
                MessageBox.Show(
                    "Для восстановления базы ЛМ ЧЗ нужны права администратора.\nПерезапустите HonestFlow от имени администратора.",
                    "Нужны права администратора",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var selectedIP = GetAuthorizedIpForManualAction();
            if (selectedIP == null)
            {
                LogOperatorAction("восстановление базы ЛМ ЧЗ отменено: авторизация не пройдена", isError: true);
                return;
            }

            if (!TryBeginLongOperation(
                "восстановление базы ЛМ ЧЗ",
                LicenseFeature.Repair,
                requiresEngineerAccess: true))
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

                bool success = await _lmDatabaseRestoreService.Restore(selectedIP);
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

        private IPData GetAuthorizedIpForManualAction()
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
                MessageBox.Show("Введите пароль точки перед ручной переустановкой.", "Авторизация", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                textBox1.Focus();
                return null;
            }

            var selectedIP = _authService.Authenticate(enteredPassword);
            if (selectedIP == null)
            {
                LogOperatorAction("ручная операция отклонена: неверный пароль", isError: true);
                MessageBox.Show("Неверный пароль!\nДоступ запрещен.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                textBox1.Clear();
                textBox1.Focus();
                return null;
            }

            _selectedIP = selectedIP;
            _logService.LogUser($"Пользователь для ручной операции: {selectedIP.Name}");
            return selectedIP;
        }

        private DiagnosticLogSelection ShowDiagnosticLogSelectionDialog()
        {
            var items = new[]
            {
                new SelectionItem<string>("system", "Сведения о системе и статусы служб"),
                new SelectionItem<string>("hf", "Логи HonestFlow"),
                new SelectionItem<string>("lm", "Логи ЛМ ЧЗ"),
                new SelectionItem<string>("esm", "Логи ЕСМ"),
                new SelectionItem<string>("kkt", "Лог ККТ / АТОЛ")
            };

            var selected = ShowCheckedSelectionDialog(
                "Сбор диагностики",
                "Выберите, какие логи включить:",
                items,
                checkAll: true);

            if (selected == null)
                return null;

            var keys = selected.Select(x => x.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selection = new DiagnosticLogSelection
            {
                IncludeSystemInfo = keys.Contains("system"),
                IncludeHonestFlow = keys.Contains("hf"),
                IncludeLm = keys.Contains("lm"),
                IncludeEsm = keys.Contains("esm"),
                IncludeKkt = keys.Contains("kkt")
            };

            if (!selection.HasAnySelection)
            {
                LogOperatorAction("сбор диагностики: оператор не выбрал ни одной группы логов");
                MessageBox.Show("Выберите хотя бы одну группу логов.", "Диагностика", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            return selection;
        }

        private IReadOnlyCollection<InstallationComponent> ShowComponentSelectionDialog()
        {
            var items = new[]
            {
                new SelectionItem<InstallationComponent>(InstallationComponent.LmModule, "ЛМ ЧЗ"),
                new SelectionItem<InstallationComponent>(InstallationComponent.AtolDriver, "Драйвер АТОЛ"),
                new SelectionItem<InstallationComponent>(InstallationComponent.Esm, "ЕСМ"),
                new SelectionItem<InstallationComponent>(InstallationComponent.Controller, "Контроллер ЛМ")
            };

            var selected = ShowCheckedSelectionDialog(
                "Ручная переустановка",
                "Выберите компоненты для переустановки:",
                items,
                checkAll: false);

            if (selected == null)
                return null;

            if (selected.Count == 0)
            {
                LogOperatorAction("ручная переустановка: оператор не выбрал ни одного компонента");
                MessageBox.Show("Выберите хотя бы один компонент.", "Ручная переустановка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            return selected.Select(x => x.Value).ToArray();
        }

        private MaintenanceAction? ShowMaintenanceActionDialog()
        {
            using var form = new Form
            {
                Text = "Обслуживание точки",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(420, 190)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(12)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var label = new Label
            {
                Text = "Выберите действие обслуживания:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var reinstallButton = new Button
            {
                Text = "Переустановить компоненты",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 4)
            };

            var restoreButton = new Button
            {
                Text = "Восстановить базу ЛМ ЧЗ",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 4)
            };

            MaintenanceAction? selected = null;

            reinstallButton.Click += (s, e) =>
            {
                selected = MaintenanceAction.ReinstallComponents;
                form.DialogResult = DialogResult.OK;
                form.Close();
            };

            restoreButton.Click += (s, e) =>
            {
                selected = MaintenanceAction.RestoreLmDatabase;
                form.DialogResult = DialogResult.OK;
                form.Close();
            };

            layout.Controls.Add(label, 0, 0);
            layout.Controls.Add(reinstallButton, 0, 1);
            layout.Controls.Add(restoreButton, 0, 2);
            form.Controls.Add(layout);

            return form.ShowDialog(this) == DialogResult.OK ? selected : null;
        }

        private static List<SelectionItem<T>> ShowCheckedSelectionDialog<T>(
            string title,
            string caption,
            SelectionItem<T>[] items,
            bool checkAll)
        {
            using var form = new Form
            {
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(420, 300)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

            var label = new Label
            {
                Text = caption,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var checkedList = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true
            };

            foreach (var item in items)
                checkedList.Items.Add(item, checkAll);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft
            };

            var okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Width = 90
            };

            var cancelButton = new Button
            {
                Text = "Отмена",
                DialogResult = DialogResult.Cancel,
                Width = 90
            };

            buttons.Controls.Add(okButton);
            buttons.Controls.Add(cancelButton);
            layout.Controls.Add(label, 0, 0);
            layout.Controls.Add(checkedList, 0, 1);
            layout.Controls.Add(buttons, 0, 2);
            form.Controls.Add(layout);
            form.AcceptButton = okButton;
            form.CancelButton = cancelButton;

            if (form.ShowDialog() != DialogResult.OK)
                return null;

            return checkedList.CheckedItems
                .Cast<SelectionItem<T>>()
                .ToList();
        }

        private static string GetComponentDisplayName(InstallationComponent component)
        {
            return component switch
            {
                InstallationComponent.LmModule => "ЛМ ЧЗ",
                InstallationComponent.AtolDriver => "Драйвер АТОЛ",
                InstallationComponent.Esm => "ЕСМ",
                InstallationComponent.Controller => "Контроллер ЛМ",
                _ => component.ToString()
            };
        }

        private sealed class SelectionItem<T>
        {
            public SelectionItem(T value, string text)
            {
                Value = value;
                Text = text;
            }

            public T Value { get; }
            private string Text { get; }

            public override string ToString() => Text;
        }

        private enum MaintenanceAction
        {
            ReinstallComponents,
            RestoreLmDatabase
        }

        private async void BtnRefreshStatus_Click(object sender, EventArgs e)
        {
            LogOperatorAction("запрошено ручное обновление статусов точки");
            if (!EnsureLicenseAccess(LicenseFeature.Diagnostics, "обновление статусов"))
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
                ShowNodeDetails(status);
                return;
            }

            if (!Utils.IsAdministrator())
            {
                LogOperatorAction("управление службами отменено: нет прав администратора", isError: true);
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

            if (!TryBeginLongOperation("управление службами", LicenseFeature.AutoFix))
                return;

            try
            {
                LogOperatorAction($"операция со службами начата: {actionName} ({serviceList})");
                _serviceActionRunning = true;
                button.Enabled = false;
                btnCheckWithoutPassword.Enabled = false;
                lblStatus.Text = $"Пытаюсь {actionName} службы: {serviceList}";

                if (shouldStart)
                    await Task.Run(() => _serviceControlService.StartStoppedServices(status.Services));
                else
                    await Task.Run(() => _serviceControlService.RestartServices(status.Services));

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
            if (!EnsureLicenseAccess(LicenseFeature.Diagnostics, "проверка состояния точки"))
                return;

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
            SetNodeChecking();
            lblStatus.Text = "Проверка служб и связи...";
            lblHeaderStatus.Text = "● Проверка";
            lblHeaderStatus.ForeColor = StatusYellow;

            try
            {
                var result = await _pointStatusService.CheckAsync(_lifetimeCancellation.Token);

                if (!allowDuringLongOperation && IsLongOperationRunning)
                {
                    _logService.LogDebug($"Результат проверки состояния точки не применен: выполняется операция \"{_longOperationName}\"");
                    return;
                }

                ApplyNodeStatus(lblLmNode, lblLmStatusText, lblLmCircle, btnLmAction, result.Lm, "ЛМ ЧЗ");
                ApplyNodeStatus(lblControllerNode, lblControllerStatusText, lblControllerCircle, btnControllerAction, result.Controller, "Контроллер");
                ApplyNodeStatus(lblEsmNode, lblEsmStatusText, lblEsmCircle, btnEsmAction, result.Esm, "ЕСМ");
                ApplyNodeStatus(lblKktNode, lblKktStatusText, lblKktCircle, btnKktAction, result.Kkt, "ККТ");
                ApplyNodeStatus(lblCloudNode, lblCloudStatusText, lblCloudCircle, btnCloudAction, result.Cloud, "Облако");
                ApplyRuDesktopStatus(result.RuDesktop);
                _lastPointStatusResult = result;
                _diagnosticArchiveService.SetPointStatusReport(BuildPointStatusDebugReport(result));
                btnPointStatusDetails.Enabled = true;

                bool hasRed = new[] { result.Lm, result.Controller, result.Esm, result.Kkt, result.Cloud, result.RuDesktop }
                    .Any(x => x.Level == NodeLevel.Error);
                bool hasYellow = new[] { result.Lm, result.Controller, result.Esm, result.Kkt, result.Cloud, result.RuDesktop }
                    .Any(x => x.Level == NodeLevel.Warning);

                lblHeaderStatus.Text = hasRed
                    ? "● Есть проблемы"
                    : hasYellow ? "● Требует внимания" : "● Всё работает";
                lblHeaderStatus.ForeColor = hasRed ? StatusRed : hasYellow ? StatusYellow : StatusGreen;
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
            }
        }

        private void SetNodeChecking()
        {
            _lastPointStatusResult = null;
            btnPointStatusDetails.Enabled = false;
            SetNodeChecking(lblLmNode, lblLmStatusText, lblLmCircle, btnLmAction, "ЛМ ЧЗ");
            SetNodeChecking(lblControllerNode, lblControllerStatusText, lblControllerCircle, btnControllerAction, "Контроллер");
            SetNodeChecking(lblEsmNode, lblEsmStatusText, lblEsmCircle, btnEsmAction, "ЕСМ");
            SetNodeChecking(lblKktNode, lblKktStatusText, lblKktCircle, btnKktAction, "ККТ");
            SetNodeChecking(lblCloudNode, lblCloudStatusText, lblCloudCircle, btnCloudAction, "Облако");
            SetNodeChecking(lblRuDesktopNode, lblRuDesktopStatusText, lblRuDesktopCircle, btnRuDesktopAction, "RuDesktop");
        }

        private void SetNodeChecking(Label nodeLabel, Label statusTextLabel, Label circle, Button actionButton, string defaultLabel)
        {
            nodeLabel.Text = defaultLabel;
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
            if (!TryBeginLongOperation("восстановление служб ЛМ ЧЗ", LicenseFeature.AutoFix))
                return;

            try
            {
                lblStatus.Text = "Запускаем службу Regime...";
                await Task.Run(() => _serviceControlService.StartService("regime"));

                lblStatus.Text = "Ожидаем запуск Yenisei через Regime...";
                await Task.Delay(TimeSpan.FromSeconds(15), _lifetimeCancellation.Token);

                if (!_serviceControlService.IsServiceRunning("yenisei"))
                {
                    lblStatus.Text = "Yenisei не запустилась автоматически. Запускаем...";
                    await Task.Run(() => _serviceControlService.StartService("yenisei"));
                }

                lblStatus.Text = "Ожидаем готовность API ЛМ ЧЗ...";
                await Task.Delay(TimeSpan.FromSeconds(15), _lifetimeCancellation.Token);
                await RefreshPointStatusAsync(allowDuringLongOperation: true);
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
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
                !TryBeginLongOperation("инициализация ЛМ ЧЗ", LicenseFeature.AutoFix))
            {
                return;
            }

            try
            {
                lblStatus.Text = "Инициализируем ЛМ ЧЗ...";
                using var api = new LmApiClient(enableDetailedLogging: false);
                ApiSimpleResponse result = await api.InitializeFull(_selectedIP.Token);
                if (!result.IsSuccess)
                {
                    MessageBox.Show(
                        $"ЛМ ЧЗ не удалось инициализировать: HTTP {(int)result.StatusCode}.",
                        "Инициализация ЛМ ЧЗ",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                lblStatus.Text = "Инициализация отправлена. Ожидаем изменение статуса...";
                await Task.Delay(TimeSpan.FromSeconds(15), _lifetimeCancellation.Token);
                await RefreshPointStatusAsync(allowDuringLongOperation: true);
                MessageBox.Show(
                    "Запрос инициализации выполнен. Актуальный результат показан в строке «ЛМ ЧЗ».",
                    "Инициализация ЛМ ЧЗ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
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

            RuDesktopPackage package = RuDesktopInstaller.GetPackageForCurrentOperatingSystem();
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
                    LicenseFeature.Install,
                    requiresEngineerAccess: false))
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

                RuDesktopInstallResult result = await _ruDesktopInstaller.InstallAsync(progress);
                if (!result.IsSuccess)
                {
                    MessageBoxIcon icon = result.Status == RuDesktopInstallStatus.UserCancelled
                        ? MessageBoxIcon.Information
                        : MessageBoxIcon.Warning;
                    MessageBox.Show(result.Message, "RuDesktop", MessageBoxButtons.OK, icon);
                    lblStatus.Text = result.Message;
                    return;
                }

                _ruDesktopService.ResetLocalConfigurationAfterInstallation();
                RuDesktopStatus updatedStatus = await _ruDesktopService.WaitForReady(
                    timeout: TimeSpan.FromSeconds(15),
                    pollInterval: TimeSpan.FromSeconds(1));
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
            if (!EnsureLicenseAccess(LicenseFeature.Diagnostics, "просмотр состояния точки"))
                return;

            if (sender is Button button && button.Tag is NodeStatus status)
            {
                LogOperatorAction($"открыты детали состояния: {status.ShortText}");
                ShowNodeDetails(status);
            }
        }

        private void ShowPointStatusDetails_Click(object sender, EventArgs e)
        {
            if (!EnsureLicenseAccess(LicenseFeature.Diagnostics, "просмотр состояния точки"))
                return;

            if (_lastPointStatusResult == null)
                return;

            LogOperatorAction("открыт общий отладочный снимок состояния точки");
            string report = BuildPointStatusDebugReport(_lastPointStatusResult);

            using var dialog = new Form
            {
                Text = "Подробнее — состояние точки",
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(820, 650),
                MinimumSize = new Size(680, 480),
                ShowIcon = false,
                ShowInTaskbar = false,
                MaximizeBox = true,
                MinimizeBox = false
            };
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10F),
                Text = report
            };
            var titleLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 14F),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(18, 12),
                Text = "Состояние всех узлов"
            };
            var subtitleLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(20, 42),
                Text = "Исходные статусы, принятые решения и доступные действия"
            };
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72,
                BackColor = Color.FromArgb(248, 250, 252)
            };
            header.Controls.Add(titleLabel);
            header.Controls.Add(subtitleLabel);
            var closeButton = new Button
            {
                Text = "Закрыть",
                DialogResult = DialogResult.OK,
                Dock = DockStyle.Right,
                Width = 120,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White
            };
            closeButton.FlatAppearance.BorderSize = 0;
            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 54,
                Padding = new Padding(0, 8, 0, 4),
                BackColor = Color.FromArgb(248, 250, 252)
            };
            footer.Controls.Add(closeButton);
            dialog.Controls.Add(textBox);
            dialog.Controls.Add(header);
            dialog.Controls.Add(footer);
            dialog.AcceptButton = closeButton;
            dialog.CancelButton = closeButton;
            dialog.ShowDialog(this);
        }

        private static string BuildPointStatusDebugReport(PointStatusResult result)
        {
            var report = new StringBuilder();
            report.AppendLine("ОТЛАДОЧНЫЙ СНИМОК СОСТОЯНИЯ ТОЧКИ");
            report.AppendLine($"Сформирован: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
            report.AppendLine("Чувствительные идентификаторы ККТ и токены намеренно не выводятся.");

            AppendNodeDebug(report, "ЛМ ЧЗ", result.Lm);
            AppendNodeDebug(report, "Контроллер", result.Controller);
            AppendNodeDebug(report, "ЕСМ", result.Esm);
            AppendNodeDebug(report, "ККТ", result.Kkt);
            AppendNodeDebug(report, "Облако", result.Cloud);
            AppendNodeDebug(report, "RuDesktop", result.RuDesktop);
            return report.ToString();
        }

        private static void AppendNodeDebug(StringBuilder report, string name, NodeStatus status)
        {
            report.AppendLine();
            report.AppendLine(new string('=', 72));
            report.AppendLine(name);
            report.AppendLine(new string('-', 72));
            if (status == null)
            {
                report.AppendLine("Данные отсутствуют.");
                return;
            }

            report.AppendLine($"Уровень: {status.Level}");
            report.AppendLine($"Короткий статус: {status.ShortText}");
            report.AppendLine($"Текст в интерфейсе: {ValueOrDash(status.StatusText)}");
            report.AppendLine($"Доступное действие: {status.ActionText}");
            report.AppendLine("Службы:");
            if (status.Services.Count == 0)
            {
                report.AppendLine("  — источник не содержит Windows-служб");
            }
            else
            {
                foreach (ServiceSnapshot service in status.Services)
                    report.AppendLine($"  — {service.ServiceName}: {service.State}");
            }

            report.AppendLine("Исходные данные и расчёт:");
            report.AppendLine(string.IsNullOrWhiteSpace(status.Details)
                ? "  — подробности отсутствуют"
                : status.Details);
        }

        private async void BtnRequestHelp_Click(object sender, EventArgs e)
        {
            LogOperatorAction("нажата кнопка запроса помощи");

            if (!EnsureLicenseAccess(LicenseFeature.SendLogs, "запрос помощи"))
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

            HelpRequestDialogResult helpRequest = ShowHelpRequestDialog();
            if (helpRequest == null)
            {
                LogOperatorAction("запрос помощи отменен оператором");
                return;
            }

            try
            {
                btnRuDesktopAction.Enabled = false;
                lblStatus.Visible = true;
                lblStatus.Text = "Отправка запроса помощи...";

                LastAuthorizedClientState lastClient = _selectedIP == null
                    ? _ruDesktopService.GetLastAuthorizedClient()
                    : null;

                HelpRequestData request = BuildHelpRequestData(helpRequest, _selectedIP, lastClient, ruDesktopId);
                await _helpRequestEmailSender.Send(request);

                MessageBox.Show(
                    "Заявка отправлена.",
                    "Запрос помощи",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                lblStatus.Text = "Заявка помощи отправлена";
            }
            catch (Exception ex)
            {
                LogOperatorAction($"не удалось отправить запрос помощи: {ex.Message}", isError: true);
                _logService.LogDebug($"Ошибка отправки запроса помощи: {ex}");
                MessageBox.Show(
                    $"Не удалось отправить заявку:\n{ex.Message}",
                    "Запрос помощи",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
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
                RuDesktopStatus status = await _ruDesktopService.GetStatus();
                if (status.IsInstalled && status.PasswordConfiguredByHonestFlow)
                    return true;

                if (!status.IsInstalled)
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
                IPData selectedClient = ResolveClientForRuDesktopSetup();
                if (selectedClient == null)
                    return false;

                await ConfigureRuDesktopPasswordFromClient(selectedClient);
                status = await _ruDesktopService.GetStatus();
                return status.IsInstalled && status.PasswordConfiguredByHonestFlow;
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

        private IPData ResolveClientForRuDesktopSetup()
        {
            if (_selectedIP != null)
                return _selectedIP;

            string enteredPassword = ShowStartupRuDesktopPasswordDialog();
            if (string.IsNullOrWhiteSpace(enteredPassword))
            {
                LogOperatorAction("запрос помощи: настройка RuDesktop отменена, пароль точки не введен");
                return null;
            }

            IPData selectedClient = _authService.Authenticate(enteredPassword);
            if (selectedClient == null)
            {
                LogOperatorAction("запрос помощи: настройка RuDesktop отклонена, неверный пароль точки", isError: true);
                MessageBox.Show("Неверный пароль точки. Запрос помощи не отправлен.", "Запрос помощи", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            try
            {
                string ruDesktopId = await _ruDesktopService.GetId();
                if (!string.IsNullOrWhiteSpace(ruDesktopId))
                    return ruDesktopId;
            }
            catch (Exception ex)
            {
                _logService.LogDebug($"RuDesktop ID для заявки помощи не получен: {ex.Message}");
            }

            string lastKnownId = _ruDesktopService.GetLastKnownId();
            LogOperatorAction(
                string.IsNullOrWhiteSpace(lastKnownId)
                    ? "RuDesktop ID для заявки помощи не получен, заявка будет отправлена без ID"
                    : "RuDesktop ID для заявки помощи взят из последнего сохраненного состояния",
                isError: string.IsNullOrWhiteSpace(lastKnownId));

            return lastKnownId;
        }

        private HelpRequestData BuildHelpRequestData(
            HelpRequestDialogResult helpRequest,
            IPData selectedClient,
            LastAuthorizedClientState lastClient,
            string ruDesktopId)
        {
            string clientName = selectedClient?.Name ?? lastClient?.Name;
            string clientInn = selectedClient?.Inn ?? lastClient?.Inn;

            return new HelpRequestData
            {
                RequestId = BuildHelpRequestId(),
                ClientName = ValueOrDash(clientName),
                InnMasked = MaskInn(clientInn),
                MachineName = Environment.MachineName,
                WindowsUser = Environment.UserName,
                RuDesktopId = ValueOrDash(ruDesktopId),
                HonestFlowVersion = System.Windows.Forms.Application.ProductVersion,
                FiscalAddress = ValueOrDash(helpRequest.FiscalAddress),
                ProblemType = ValueOrDash(helpRequest.ProblemType),
                Message = ValueOrDash(helpRequest.Message),
                CreatedAt = DateTimeOffset.Now.ToString("o")
            };
        }

        private static string BuildHelpRequestId()
        {
            return $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}".Substring(0, 20).ToUpperInvariant();
        }

        private HelpRequestDialogResult ShowHelpRequestDialog()
        {
            PointAddressResult pointAddress = ResolveCurrentPointAddress();
            string fiscalAddress = pointAddress.Address;
            bool addressFound = !string.IsNullOrWhiteSpace(fiscalAddress);

            using var form = new Form
            {
                Text = "Запросить помощь",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(460, 390)
            };

            var typeLabel = new Label
            {
                Text = "Тип проблемы:",
                Location = new Point(16, 18),
                AutoSize = true
            };

            var problemTypeBox = new ComboBox
            {
                Location = new Point(16, 42),
                Width = 428,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            problemTypeBox.Items.AddRange(new object[]
            {
                "Ошибка проверки кода маркировки",
                "Ошибка ККТ",
                "Ошибка Кассового ПО",
                "Другое"
            });
            problemTypeBox.SelectedIndex = 0;

            var addressLabel = new Label
            {
                Text = "Адрес точки:",
                Location = new Point(16, 82),
                AutoSize = true
            };

            var addressBox = new TextBox
            {
                Location = new Point(16, 106),
                Width = 428,
                Text = addressFound ? fiscalAddress : string.Empty
            };

            var addressHintLabel = new Label
            {
                Text = addressFound
                    ? "Адрес точки найден автоматически, проверьте его перед отправкой."
                    : "Адрес точки не указан. Введите его вручную, пожалуйста.",
                Location = new Point(16, 132),
                Size = new Size(428, 34),
                ForeColor = addressFound ? Color.FromArgb(71, 85, 105) : Color.FromArgb(180, 83, 9)
            };

            var messageLabel = new Label
            {
                Text = "Сообщение:",
                Location = new Point(16, 174),
                AutoSize = true
            };

            var messageBox = new TextBox
            {
                Location = new Point(16, 198),
                Width = 428,
                Height = 140,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical
            };

            var okButton = new Button
            {
                Text = "Отправить",
                DialogResult = DialogResult.OK,
                Location = new Point(254, 344),
                Width = 90
            };

            var cancelButton = new Button
            {
                Text = "Отмена",
                DialogResult = DialogResult.Cancel,
                Location = new Point(354, 344),
                Width = 90
            };

            form.Controls.Add(typeLabel);
            form.Controls.Add(problemTypeBox);
            form.Controls.Add(addressLabel);
            form.Controls.Add(addressBox);
            form.Controls.Add(addressHintLabel);
            form.Controls.Add(messageLabel);
            form.Controls.Add(messageBox);
            form.Controls.Add(okButton);
            form.Controls.Add(cancelButton);
            form.AcceptButton = okButton;
            form.CancelButton = cancelButton;

            if (form.ShowDialog() != DialogResult.OK)
                return null;

            _pointAddressService.Save(
                _licenseSnapshotStore.Current?.DeviceId,
                addressBox.Text,
                PointAddressSource.Manual);

            return new HelpRequestDialogResult
            {
                ProblemType = problemTypeBox.Text,
                FiscalAddress = addressBox.Text,
                Message = messageBox.Text
            };
        }

        private PointAddressResult ResolveCurrentPointAddress()
        {
            return _pointAddressService.Resolve(_licenseSnapshotStore.Current);
        }

        private static string MaskInn(string inn)
        {
            if (string.IsNullOrWhiteSpace(inn))
                return "-";

            inn = inn.Trim();
            if (inn.Length <= 4)
                return new string('*', inn.Length);

            int left = Math.Min(2, inn.Length);
            int right = Math.Min(2, inn.Length - left);
            return inn.Substring(0, left) +
                   new string('*', Math.Max(0, inn.Length - left - right)) +
                   inn.Substring(inn.Length - right);
        }

        private static string ValueOrDash(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }

        private sealed class HelpRequestDialogResult
        {
            public string ProblemType { get; set; }
            public string FiscalAddress { get; set; }
            public string Message { get; set; }
        }

        private static void ShowNodeDetails(NodeStatus status)
        {
            if (!string.IsNullOrWhiteSpace(status?.Details))
            {
                MessageBoxIcon icon = status.Level switch
                {
                    NodeLevel.Error => MessageBoxIcon.Warning,
                    NodeLevel.Warning => MessageBoxIcon.Information,
                    _ => MessageBoxIcon.Information
                };
                string title = status.Level == NodeLevel.Error
                    ? "Требуется внимание"
                    : "Подробности проверки";
                MessageBox.Show(status.Details, title, MessageBoxButtons.OK, icon);
            }
        }

        private void BtnDetails_Click(object sender, EventArgs e)
        {
            LogOperatorAction("открытие журнала выполнения");

            if (!EnsureLicenseAccess(LicenseFeature.Diagnostics, "открытие журнала"))
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
                MessageBox.Show("Файл лога не найден.", "Журнал выполнения", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            btnStartInstallation.Text = selectedIP.EngineerAccess == null
                ? "Запустить установку"
                : "🔒 Запустить установку";
            btnStartInstallation.Visible = true;
            btnMaintenance.Visible = true;
            leftLayout.RowStyles[9].Height = 40;
            leftLayout.RowStyles[10].Height = 0;
            btnRateApplication.Visible = true;
            btnRateApplication.Enabled = true;
            lblRatingThanks.Visible = false;
            btnRefreshLicense.Enabled = true;
            btnMaintenance.Text = selectedIP.EngineerAccess == null
                ? "Обслуживание точки"
                : "🔒 Обслуживание точки";
            btnDetails.Visible = true;
            lblStatus.Text = "Лицензия проверена. Активен режим продавца.";
            lblAuthorizedClient.Text = "Авторизован: " + selectedIP.Name;

            _logService.LogUser($"Пользователь: {selectedIP.Name}");
            _logService.LogDebug($"Авторизован: {selectedIP.Name}, ИНН: {selectedIP.Inn}, Разрядность: {selectedIP.Architecture}");
            _ruDesktopService.SaveLastAuthorizedClient(selectedIP);
            ApplyLicenseAccessToUi();
            HandleLicenseSnapshot(_licenseSnapshotStore.Current);
        }

        private async Task OfferStartupRuDesktopSetupIfNeeded()
        {
            if (_selectedIP != null || IsLongOperationRunning)
                return;

            bool needsSetup = await _ruDesktopService.NeedsInitialPasswordSetup();
            if (!needsSetup)
                return;

            LogOperatorAction("RuDesktop: требуется первичная настройка постоянного пароля");

            string enteredPassword = ShowStartupRuDesktopPasswordDialog();
            if (string.IsNullOrWhiteSpace(enteredPassword))
            {
                LogOperatorAction("RuDesktop: первичная настройка пропущена, пароль точки не введен");
                return;
            }

            var selectedIP = _authService.Authenticate(enteredPassword);
            if (selectedIP == null)
            {
                LogOperatorAction("RuDesktop: первичная настройка отклонена, неверный пароль точки", isError: true);
                MessageBox.Show("Неверный пароль точки. RuDesktop не настроен.", "Настройка RuDesktop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        private string ShowStartupRuDesktopPasswordDialog()
        {
            using var form = new Form
            {
                Text = "Настройка RuDesktop",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(430, 180)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(14)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var label = new Label
            {
                Text = "RuDesktop установлен, но постоянный пароль ещё не настроен.\nВведите пароль точки, чтобы настроить доступ.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var passwordLabel = new Label
            {
                Text = "Пароль точки",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.BottomLeft
            };

            var passwordBox = new TextBox
            {
                Dock = DockStyle.Fill,
                UseSystemPasswordChar = true
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft
            };

            var okButton = new Button
            {
                Text = "Настроить",
                DialogResult = DialogResult.OK,
                Width = 100
            };

            var cancelButton = new Button
            {
                Text = "Позже",
                DialogResult = DialogResult.Cancel,
                Width = 90
            };

            buttons.Controls.Add(okButton);
            buttons.Controls.Add(cancelButton);
            layout.Controls.Add(label, 0, 0);
            layout.Controls.Add(passwordLabel, 0, 1);
            layout.Controls.Add(passwordBox, 0, 2);
            layout.Controls.Add(buttons, 0, 3);
            form.Controls.Add(layout);
            form.AcceptButton = okButton;
            form.CancelButton = cancelButton;

            return form.ShowDialog(this) == DialogResult.OK ? passwordBox.Text : null;
        }

        private async Task ConfigureRuDesktopPasswordFromClient(IPData selectedIP)
        {
            if (!TryBeginLongOperation("настройка RuDesktop", LicenseFeature.ManualTools))
                return;

            try
            {
                LogOperatorAction($"RuDesktop: запуск настройки постоянного пароля для клиента {selectedIP.Name}");
                lblStatus.Visible = true;
                lblStatus.Text = "Настройка RuDesktop...";

                RuDesktopSetupResult result = await _ruDesktopService.ConfigurePermanentPassword(selectedIP.RuDesktop.Password);
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
            if (!_ruDesktopService.ShouldOfferPasswordSetup(selectedIP))
                return;

            string ruDesktopId = await _ruDesktopService.GetId();
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
            MessageBox.Show(message, "Операция уже выполняется", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        private bool TryBeginLongOperation(
            string operationName,
            LicenseFeature? requiredFeature = null,
            bool requiresEngineerAccess = false)
        {
            if (requiredFeature.HasValue && !EnsureLicenseAccess(requiredFeature.Value, operationName))
                return false;

            if (requiresEngineerAccess && !EnsureEngineerAccess(operationName))
                return false;

            if (!EnsureNoLongOperation(operationName))
                return false;

            _longOperationName = operationName;
            SetLongOperationControlsEnabled(false);
            return true;
        }

        private bool EnsureEngineerAccess(string operationName)
        {
            EngineerAccessResult access = _engineerAccessService.CheckAccess(_selectedIP);
            if (access.IsAllowed)
            {
                if (access.TechnicalCode == "ENGINEER_PASSWORD_NOT_CONFIGURED_LEGACY_ALLOWED")
                {
                    Logger.Warning(
                        $"Event=EngineerAccessLegacyAllowed Operation={operationName}",
                        nameof(MainForm));
                }
                return true;
            }

            if (!access.PasswordRequired)
            {
                LogEngineerAccessDenied(operationName, access);
                return false;
            }

            string password = ShowEngineerPasswordDialog(operationName);
            if (password == null)
                return false;

            access = _engineerAccessService.Unlock(_selectedIP, password);
            if (!access.IsAllowed)
            {
                LogEngineerAccessDenied(operationName, access);
                return false;
            }

            Logger.Info(
                $"Event=EngineerAccessGranted TechnicalCode={access.TechnicalCode}",
                nameof(MainForm));
            ApplyLicenseAccessToUi();
            Logger.Info(
                $"Event=EngineerUiRefreshed InstallEnabled={btnStartInstallation.Enabled} " +
                $"MaintenanceEnabled={btnMaintenance.Enabled}",
                nameof(MainForm));
            return true;
        }

        private void LogEngineerAccessDenied(string operationName, EngineerAccessResult access)
        {
            Logger.Warning(
                $"Event=EngineerAccessDenied TechnicalCode={access.TechnicalCode}",
                nameof(MainForm));
            LogOperatorAction(
                $"{operationName} отклонено инженерной политикой (код: {access.TechnicalCode})",
                isError: true);
            MessageBox.Show(
                access.Message,
                "Инженерный доступ",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private string ShowEngineerPasswordDialog(string operationName)
        {
            using var form = new Form
            {
                Text = "Режим инженера",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(430, 180)
            };
            var description = new Label
            {
                Left = 20,
                Top = 18,
                Width = 390,
                Height = 45,
                Text = "Операция: " + operationName + "\nВведите пароль инженера."
            };
            var passwordBox = new TextBox
            {
                Left = 20,
                Top = 72,
                Width = 390,
                UseSystemPasswordChar = true
            };
            var ok = new Button
            {
                Left = 230,
                Top = 122,
                Width = 85,
                Height = 32,
                Text = "Открыть",
                DialogResult = DialogResult.OK
            };
            var cancel = new Button
            {
                Left = 325,
                Top = 122,
                Width = 85,
                Height = 32,
                Text = "Отмена",
                DialogResult = DialogResult.Cancel
            };
            form.Controls.AddRange(new Control[] { description, passwordBox, ok, cancel });
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            form.Shown += (_, _) => passwordBox.Focus();
            return form.ShowDialog(this) == DialogResult.OK ? passwordBox.Text : null;
        }

        private void EndLongOperation()
        {
            _longOperationName = null;
            SetLongOperationControlsEnabled(true);
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

        private bool EnsureLicenseAccess(LicenseFeature feature, string operationName)
        {
            LicenseAccessResult access = _licenseAccessPolicy.Check(feature);
            if (access.IsAllowed)
                return true;

            Logger.Warning(
                $"Event=LicenseOperationDenied Feature={feature} TechnicalCode={access.TechnicalCode}",
                nameof(MainForm));
            LogOperatorAction($"{operationName} заблокировано лицензией (код: {access.TechnicalCode})", isError: true);
            MessageBox.Show(
                access.Message,
                "Функция недоступна",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        private void ApplyLicenseAccessToUi()
        {
            if (IsDisposed || IsLongOperationRunning)
                return;

            SetFeatureAvailability(btnDiagnostics, LicenseFeature.Diagnostics);
            SetFeatureAvailability(btnCheckWithoutPassword, LicenseFeature.Diagnostics);
            SetFeatureAvailability(btnDetails, LicenseFeature.Diagnostics);
            SetFeatureAvailability(btnMaintenance, LicenseFeature.Repair);
            SetFeatureAvailability(btnReinstallComponents, LicenseFeature.Repair);
            SetFeatureAvailability(btnRestoreLmDatabase, LicenseFeature.Repair);
            SetFeatureAvailability(btnOpenKktDriver, LicenseFeature.ManualTools);
            SetFeatureAvailability(btnOpenEsm, LicenseFeature.ManualTools);

            if (_selectedIP != null)
                SetFeatureAvailability(btnStartInstallation, LicenseFeature.Install);

            ApplyNodeLicenseAccess(btnLmAction);
            ApplyNodeLicenseAccess(btnControllerAction);
            SetFeatureAvailability(btnPointStatusDetails, LicenseFeature.Diagnostics);
            ApplyNodeLicenseAccess(btnEsmAction);
            ApplyNodeLicenseAccess(btnKktAction);
            ApplyNodeLicenseAccess(btnCloudAction);
            ApplyRuDesktopLicenseAccess();
        }

        private void ApplyRuDesktopLicenseAccess()
        {
            LicenseFeature feature = btnRuDesktopAction.Tag is NodeStatus status
                ? status.ActionKind switch
                {
                    NodeActionKind.InstallRuDesktop => LicenseFeature.Install,
                    NodeActionKind.ReinstallRuDesktop => LicenseFeature.Install,
                    NodeActionKind.ManageServices => LicenseFeature.AutoFix,
                    NodeActionKind.RequestRuDesktopHelp => LicenseFeature.SendLogs,
                    _ => LicenseFeature.Diagnostics
                }
                : LicenseFeature.Diagnostics;

            SetFeatureAvailability(btnRuDesktopAction, feature);
        }

        private void ApplyNodeLicenseAccess(Button button)
        {
            LicenseFeature feature = button.Tag is NodeStatus status &&
                                     (status.CanManageServices ||
                                      status.ActionKind == NodeActionKind.RecoverLmServices ||
                                      status.ActionKind == NodeActionKind.InitializeLm)
                ? LicenseFeature.AutoFix
                : LicenseFeature.Diagnostics;
            SetFeatureAvailability(button, feature);
        }

        private void SetFeatureAvailability(Control control, LicenseFeature feature)
        {
            LicenseAccessResult access = _licenseAccessPolicy.Check(feature);
            control.Enabled = access.IsAllowed;
            _licenseToolTip.SetToolTip(control, access.IsAllowed ? string.Empty : access.Message);
        }

        private void LicenseSnapshotChanged(LicenseObservationSnapshot snapshot)
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
            }
            catch (InvalidOperationException)
            {
                // Окно уже закрывается; решение сохранено в snapshot store и в журнале.
            }
        }

        private void HandleLicenseSnapshot(LicenseObservationSnapshot snapshot)
        {
            _pointAddressService.Resolve(snapshot);

            if (snapshot?.Decision == LicenseDecision.DeviceNotRegistered && _selectedIP != null)
                _ = SendDeviceRegistrationRequestSafelyAsync(snapshot);

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
            switch (snapshot.Decision)
            {
                case LicenseDecision.Allowed:
                    lblStatus.Text = "Лицензия проверена. Доступные функции применены.";
                    return;

                case LicenseDecision.DeviceNotRegistered:
                    ShowUnregisteredDevice(snapshot);
                    return;

                case LicenseDecision.ClientDisabled:
                    ShowLicenseWarning("Клиент отключён", "Лицензия клиента отключена. Доступны диагностика и отправка логов.");
                    return;

                case LicenseDecision.DeviceDisabled:
                    ShowLicenseWarning("Устройство отключено", "Это устройство отключено в лицензии. Доступны диагностика и отправка логов.");
                    return;

                case LicenseDecision.VersionTooOld:
                    string minimumVersion = snapshot.MinimumRequiredVersion?.ToString() ?? "указанной в лицензии";
                    ShowLicenseWarning(
                        "Требуется обязательное обновление",
                        $"Текущая версия HonestFlow устарела. Обновите программу до версии {minimumVersion} или новее. До обновления доступны только диагностические функции.");
                    return;

                case LicenseDecision.OfflineGraceExpired:
                    string lastCheck = snapshot.LastSuccessfulOnlineCheckUtc.HasValue
                        ? snapshot.LastSuccessfulOnlineCheckUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss")
                        : "неизвестно";
                    ShowLicenseWarning(
                        "Истёк автономный период",
                        $"Offline grace period истёк. Последняя успешная онлайн-проверка: {lastCheck}. Доступны диагностика и отправка логов.");
                    return;

                case LicenseDecision.InvalidLicenseState:
                    ShowLicenseWarning(
                        "Диагностический режим",
                        "Состояние лицензии не удалось надёжно определить. HonestFlow продолжит работу в безопасном диагностическом режиме.");
                    return;

                default:
                    ShowLicenseWarning(
                        "Ограниченный режим",
                        string.IsNullOrWhiteSpace(snapshot.Message)
                            ? "Лицензия не разрешает изменяющие систему операции. Доступны диагностические функции."
                            : snapshot.Message);
                    return;
            }
        }

        private void ShowUnregisteredDevice(LicenseObservationSnapshot snapshot)
        {
            string clientId = string.IsNullOrWhiteSpace(snapshot.ClientId) ? "не указан" : snapshot.ClientId;
            string deviceId = string.IsNullOrWhiteSpace(snapshot.DeviceId) ? "не удалось получить" : snapshot.DeviceId;
            DialogResult copy = MessageBox.Show(
                $"Устройство не зарегистрировано в лицензии.\n\nClientId: {clientId}\nDeviceId: {deviceId}\n\nСкопировать заявку на регистрацию?",
                "Устройство не зарегистрировано",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (copy == DialogResult.Yes &&
                !string.IsNullOrWhiteSpace(snapshot.ClientId) &&
                !string.IsNullOrWhiteSpace(snapshot.DeviceId))
            {
                string pointAddress = _pointAddressService.Resolve(snapshot).Address;
                string request = _deviceRegistrationRequestService.Create(
                    snapshot.ClientId,
                    snapshot.DeviceId,
                    Environment.MachineName,
                    pointAddress,
                    GetHonestFlowVersion(),
                    DateTimeOffset.UtcNow);
                Clipboard.SetText(request);
                lblStatus.Text = "Заявка на регистрацию устройства скопирована.";
            }
        }

        private async Task SendDeviceRegistrationRequestSafelyAsync(
            LicenseObservationSnapshot snapshot)
        {
            string pointAddress = _pointAddressService.Resolve(snapshot).Address;
            DeviceRegistrationDeliveryStatus status = await _deviceRegistrationCoordinator.TrySendAsync(
                snapshot,
                Environment.MachineName,
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
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static string GetHonestFlowVersion() =>
            typeof(MainForm).Assembly.GetName().Version?.ToString() ?? "unknown";

        private void LogOperatorAction(string action, bool isError = false)
        {
            _logService.LogUser($"Оператор: {action}", isError);
        }

        private static string DescribeDiagnosticSelection(DiagnosticLogSelection selection)
        {
            var groups = new List<string>();

            if (selection.IncludeSystemInfo)
                groups.Add("система");
            if (selection.IncludeHonestFlow)
                groups.Add("HonestFlow");
            if (selection.IncludeLm)
                groups.Add("ЛМ ЧЗ");
            if (selection.IncludeEsm)
                groups.Add("ЕСМ");
            if (selection.IncludeKkt)
                groups.Add("ККТ/АТОЛ");

            return groups.Count == 0 ? "ничего не выбрано" : string.Join(", ", groups);
        }
    }
}
