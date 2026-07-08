using HonestFlow.Application.Bootstrap;
using HonestFlow.Infrastructure;
using HonestFlow.Infrastructure.Dialogs;
using HonestFlow.Models;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Core;
using HonestFlow.Application.Diagnostics;
using HonestFlow.Application.Installation;
using HonestFlow.Application.Installation.Planning;
using HonestFlow.Application.Lm;
using HonestFlow.Application.PointStatus;
using HonestFlow.Application.RemoteAccess;
using HonestFlow.Application.Ui;
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
        private bool _useRemoteConfigMode = false;
        private List<IPData> _remoteIps;
        private VersionsData _remoteVersions;
        private IPData _selectedIP;

        private readonly DiagnosticArchiveService _diagnosticArchiveService;
        private readonly DiagnosticsEmailSender _diagnosticsEmailSender;
        private readonly LmDatabaseRestoreService _lmDatabaseRestoreService;
        private readonly RuDesktopService _ruDesktopService;
        private readonly HelpRequestEmailSender _helpRequestEmailSender;
        private readonly WindowsServiceControlService _serviceControlService;
        private readonly ExternalApplicationLauncher _externalApplicationLauncher;
        private readonly WindowIconService _windowIconService;
        private readonly IUserDialogService _dialogService;
        private PointStatusService _pointStatusService;
        private bool _statusRefreshRunning;
        private bool _serviceActionRunning;
        private string _longOperationName;

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
            _diagnosticArchiveService = new DiagnosticArchiveService(_logService);
            _diagnosticsEmailSender = new DiagnosticsEmailSender(_logService, _ruDesktopService);
            _helpRequestEmailSender = new HelpRequestEmailSender(_logService);
            _serviceControlService = new WindowsServiceControlService();
            _externalApplicationLauncher = new ExternalApplicationLauncher();
            _windowIconService = new WindowIconService(_logService);

            startup ??= new ApplicationStartupService(_logService, _progressService, _dialogService).Start();
            _useRemoteConfigMode = startup.UseRemoteConfigMode;
            _remoteIps = startup.Ips ?? startup.RemoteIps;
            _remoteVersions = startup.RemoteVersions;
            _authService = startup.AuthService;
            _installationService = new InstallationService(_logService, _progressService, _dialogService, _useRemoteConfigMode);
            _lmDatabaseRestoreService = new LmDatabaseRestoreService(_logService, _progressService, _dialogService, _useRemoteConfigMode);
            _pointStatusService = new PointStatusService(_useRemoteConfigMode, _remoteIps?.Count ?? 0, _remoteIps, _ruDesktopService);

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
            statusTextLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            statusTextLabel.ForeColor = System.Drawing.Color.FromArgb(51, 65, 85);
            statusTextLabel.Padding = new System.Windows.Forms.Padding(6, 0, 0, 0);
            statusTextLabel.Text = "Ожидание";
            statusTextLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            statusCircle.Dock = System.Windows.Forms.DockStyle.Fill;
            statusCircle.Font = new System.Drawing.Font("Segoe UI", 18F, System.Drawing.FontStyle.Bold);
            statusCircle.ForeColor = circleColor;
            statusCircle.Text = "●";
            statusCircle.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;

            actionButton.Dock = System.Windows.Forms.DockStyle.Fill;
            actionButton.Margin = new System.Windows.Forms.Padding(10, 8, 10, 8);
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
            button2.Text = "Войти";

            lblStatus.Text = "Ожидание запуска проверки";
            lblHeaderStatus.Text = "● Ожидание проверки";
            lblCloudNode.Text = "Связь с облаком";
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

            btnReinstallComponents.Click += BtnReinstallComponents_Click;
            btnRestoreLmDatabase.Click += BtnRestoreLmDatabase_Click;
            btnMaintenance.Click += BtnMaintenance_Click;

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
            LogOperatorAction($"открытие внешнего приложения: {title}");

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
            if (_selectedIP != null)
            {
                await StartInstallationForAuthorizedUser();
                return;
            }

            LogOperatorAction("нажата кнопка входа");

            if (!EnsureNoLongOperation("вход"))
                return;

            string enteredPassword = textBox1.Text;

            if (string.IsNullOrWhiteSpace(enteredPassword))
            {
                LogOperatorAction("вход отменен: пароль не введен", isError: true);
                MessageBox.Show("Введите пароль!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                textBox1.Focus();
                return;
            }

            var selectedIP = _authService.Authenticate(enteredPassword);
            if (selectedIP == null)
            {
                LogOperatorAction("вход отклонен: неверный пароль", isError: true);
                MessageBox.Show("Неверный пароль!\nДоступ запрещен.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                textBox1.Clear();
                textBox1.Focus();
                return;
            }

            _selectedIP = selectedIP;
            MessageBox.Show($"Добро пожаловать, {selectedIP.Name}!", "Успешный вход", MessageBoxButtons.OK, MessageBoxIcon.Information);

            textBox1.Enabled = false;
            button2.Text = "Запустить";
            btnDetails.Visible = true;
            lblStatus.Text = "Вход выполнен. Можно запускать проверку.";

            _logService.LogUser($"Пользователь: {selectedIP.Name}");
            _logService.LogDebug($"Авторизован: {selectedIP.Name}, ИНН: {selectedIP.Inn}, Разрядность: {selectedIP.Architecture}");
            _ruDesktopService.SaveLastAuthorizedClient(selectedIP);

            await OfferRuDesktopPasswordSetupIfNeeded(selectedIP);
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

            if (!TryBeginLongOperation("проверка и установка компонентов"))
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

            if (!TryBeginLongOperation("сбор диагностики"))
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

                archiveInfo = await Task.Run(() => _diagnosticArchiveService.CreateArchiveInfo(selection));
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

            if (!TryBeginLongOperation("ручная переустановка компонентов"))
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

            if (!TryBeginLongOperation("восстановление базы ЛМ ЧЗ"))
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

            if (!TryBeginLongOperation("управление службами"))
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
                var result = await Task.Run(() => _pointStatusService.Check());

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
            }
        }

        private void SetNodeChecking()
        {
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
            actionButton.Click += status.CanManageServices ? ServiceAction_Click : ShowNodeDetails_Click;
        }

        private void ApplyRuDesktopStatus(NodeStatus status)
        {
            ApplyNodeStatus(lblRuDesktopNode, lblRuDesktopStatusText, lblRuDesktopCircle, btnRuDesktopAction, status, "RuDesktop");
            btnRuDesktopAction.Click -= ShowNodeDetails_Click;
            btnRuDesktopAction.Click -= ServiceAction_Click;
            btnRuDesktopAction.Click -= BtnRefreshStatus_Click;
            btnRuDesktopAction.Click -= BtnRequestHelp_Click;

            if (status.Level == NodeLevel.Ok || status.Level == NodeLevel.Warning)
            {
                btnRuDesktopAction.Text = "Запросить помощь";
                btnRuDesktopAction.Click += BtnRequestHelp_Click;
            }
            else
            {
                btnRuDesktopAction.Text = "Подробнее";
                btnRuDesktopAction.Click += ShowNodeDetails_Click;
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
            if (sender is Button button && button.Tag is NodeStatus status)
            {
                LogOperatorAction($"открыты детали состояния: {status.ShortText}");
                ShowNodeDetails(status);
            }
        }

        private async void BtnRequestHelp_Click(object sender, EventArgs e)
        {
            LogOperatorAction("нажата кнопка запроса помощи");

            if (!EnsureNoLongOperation("запрос помощи"))
                return;

            try
            {
                btnRuDesktopAction.Enabled = false;
                lblStatus.Visible = true;
                lblStatus.Text = "Отправка запроса помощи...";

                string ruDesktopId = await _ruDesktopService.GetId();
                if (string.IsNullOrWhiteSpace(ruDesktopId))
                {
                    LogOperatorAction("запрос помощи отменен: не удалось получить RuDesktop ID", isError: true);
                    MessageBox.Show(
                        "Не удалось получить RuDesktop ID. Проверьте, что RuDesktop установлен и запущен.",
                        "Запрос помощи",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                LastAuthorizedClientState lastClient = _selectedIP == null
                    ? _ruDesktopService.GetLastAuthorizedClient()
                    : null;

                await _helpRequestEmailSender.Send(_selectedIP, lastClient, ruDesktopId);

                MessageBox.Show(
                    "Запрос помощи отправлен.\n\n" +
                    $"RuDesktop ID: {ruDesktopId}",
                    "Запрос помощи",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                lblStatus.Text = "Запрос помощи отправлен";
            }
            catch (Exception ex)
            {
                LogOperatorAction($"не удалось отправить запрос помощи: {ex.Message}", isError: true);
                _logService.LogDebug($"Ошибка отправки запроса помощи: {ex}");
                MessageBox.Show(
                    $"Не удалось отправить запрос помощи:\n{ex.Message}",
                    "Запрос помощи",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                lblStatus.Text = $"Ошибка запроса помощи: {ex.Message}";
            }
            finally
            {
                btnRuDesktopAction.Enabled = true;
            }
        }

        private static void ShowNodeDetails(NodeStatus status)
        {
            if (!string.IsNullOrWhiteSpace(status?.Details))
                MessageBox.Show(status.Details, "Состояние точки", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BtnDetails_Click(object sender, EventArgs e)
        {
            LogOperatorAction("открытие журнала выполнения");

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

            if (!TryBeginLongOperation("настройка RuDesktop"))
                return;

            try
            {
                LogOperatorAction($"RuDesktop: запуск настройки постоянного пароля, ID: {ruDesktopId}");
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

        private bool EnsureNoLongOperation(string requestedAction)
        {
            if (!IsLongOperationRunning)
                return true;

            string message = $"Сейчас выполняется операция: {_longOperationName}. Дождитесь завершения.";
            LogOperatorAction($"{requestedAction} не запущено: {message}");
            MessageBox.Show(message, "Операция уже выполняется", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        private bool TryBeginLongOperation(string operationName)
        {
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
        }

        private void SetLongOperationControlsEnabled(bool enabled)
        {
            bool authEnabled = enabled && _selectedIP == null;

            textBox1.Enabled = authEnabled;
            button2.Enabled = enabled;
            btnCheckWithoutPassword.Enabled = enabled;
            btnDiagnostics.Enabled = enabled;
            btnMaintenance.Enabled = enabled;
            btnReinstallComponents.Enabled = enabled;
            btnRestoreLmDatabase.Enabled = enabled;
            btnOpenKktDriver.Enabled = enabled;
            btnOpenEsm.Enabled = enabled;

            SetNodeActionButtonsEnabled(enabled);
        }

        private void SetNodeActionButtonsEnabled(bool enabled)
        {
            btnLmAction.Enabled = enabled;
            btnControllerAction.Enabled = enabled;
            btnEsmAction.Enabled = enabled;
            btnKktAction.Enabled = enabled;
            btnCloudAction.Enabled = enabled;
            btnRuDesktopAction.Enabled = enabled;
        }

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
