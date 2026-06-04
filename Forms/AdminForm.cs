using ESM_Installer_SPI;
using ESM_Installer_SPI.Models;
using HonestFlow.Infrastructure;
using HonestFlow.Models;
using HonestFlow.Services.Core;
using HonestFlow.Services.System;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HonestFlow
{
    public partial class AdminForm : Form
    {
        private List<IPData> _ips;
        private VersionsData _versions;

        private TextBox txtSearchIp;
        private Button btnSearchClear;

        private readonly ISystemService _systemService;
        private readonly ILogService _logService;

        public AdminForm()
        {
            InitializeComponent();

            _logService = new LogService();
            _systemService = new SystemService(_logService);

            LoadData();
            SetupSearchBox();
            LoadSystemInfo();

            // Загружаем статус службы
            Task.Run(async () => await UpdateServiceStatus());
        }

        private async void BtnStopService_Click(object sender, EventArgs e)
        {
            await ManageService("stop");
        }

        private async void BtnStartService_Click(object sender, EventArgs e)
        {
            await ManageService("start");
        }

        private async void BtnRestartService_Click(object sender, EventArgs e)
        {
            await ManageService("restart");
        }

        private async void BtnCheckApi_Click(object sender, EventArgs e)
        {
            await CheckApi();
        }

        private void BtnOpenLogs_Click(object sender, EventArgs e)
        {
            OpenLogsFolder();
        }

        private void BtnRefreshSystem_Click(object sender, EventArgs e)
        {
            LoadSystemInfo();
        }

        private void BtnCopySystemInfo_Click(object sender, EventArgs e)
        {
            Clipboard.SetText(txtSystemInfo.Text);
            MessageBox.Show("Скопировано!", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async Task ManageService(string action)
        {
            bool success = await _systemService.ManageService(action);
            if (!success)
            {
                MessageBox.Show($"Не удалось выполнить действие '{action}'", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            await UpdateServiceStatus();
        }

        private async Task UpdateServiceStatus()
        {
            string status = await _systemService.GetServiceStatus();

            if (lblServiceStatus.InvokeRequired)
            {
                lblServiceStatus.Invoke(new Action(() =>
                {
                    lblServiceStatus.Text = status switch
                    {
                        "running" => "Статус: ✅ РАБОТАЕТ",
                        "stopped" => "Статус: ⏹️ ОСТАНОВЛЕНА",
                        "notfound" => "Статус: ❌ СЛУЖБА НЕ НАЙДЕНА",
                        _ => "Статус: ❓ НЕИЗВЕСТНО"
                    };
                }));
            }
        }

        private async Task CheckApi()
        {
            try
            {
                bool isAvailable = await _systemService.IsApiAvailable();
                if (isAvailable)
                {
                    var status = await _systemService.GetApiStatus();
                    MessageBox.Show($"API доступен!\n\nВерсия: {status?.version}\nСтатус: {status?.status}\nИНН: {status?.inn ?? "не задан"}",
                        "Результат", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("API НЕ ДОСТУПЕН!\nПроверьте, запущена ли служба Regime.",
                        "Результат", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                _logService.LogDebug($"CheckApi ошибка: {ex.Message}");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenLogsFolder()
        {
            string logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            if (System.IO.Directory.Exists(logFolder))
                System.Diagnostics.Process.Start("explorer.exe", logFolder);
            else
                MessageBox.Show("Папка с логами не найдена", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void LoadSystemInfo()
        {
            txtSystemInfo.Text = _systemService.GetSystemInfo();
        }

        // ========== РАБОТА С ИП, ВЕРСИЯМИ, КОНФИГАМИ ==========

        private void DataGridViewIps_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            ConfigManager.SaveIps(_ips);
            dataGridViewIps.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.LightGreen;

            var row = dataGridViewIps.Rows[e.RowIndex];
            var timer = new Timer();
            timer.Interval = 1000;
            timer.Tick += (s, args) =>
            {
                row.DefaultCellStyle.BackColor = Color.White;
                timer.Stop();
            };
            timer.Start();
        }

        private void LoadData()
        {
            _ips = ConfigManager.LoadIps();
            _versions = ConfigManager.LoadVersions();

            dataGridViewIps.AutoGenerateColumns = true;
            dataGridViewIps.DataSource = null;
            dataGridViewIps.DataSource = _ips;
            dataGridViewIps.ReadOnly = false;
            dataGridViewIps.EditMode = DataGridViewEditMode.EditOnEnter;
            dataGridViewIps.CellEndEdit += DataGridViewIps_CellEndEdit;

            if (dataGridViewIps.Columns["Name"] != null)
                dataGridViewIps.Columns["Name"].HeaderText = "Имя ИП";
            if (dataGridViewIps.Columns["Password"] != null)
                dataGridViewIps.Columns["Password"].HeaderText = "Пароль";
            if (dataGridViewIps.Columns["Token"] != null)
                dataGridViewIps.Columns["Token"].HeaderText = "Токен";
            if (dataGridViewIps.Columns["Inn"] != null)
                dataGridViewIps.Columns["Inn"].HeaderText = "ИНН";
            if (dataGridViewIps.Columns["Architecture"] != null)
                dataGridViewIps.Columns["Architecture"].HeaderText = "Разрядность";

            txtLmVersion.Text = _versions?.lm_module ?? "";
            txtAtolVersion.Text = _versions?.atol_driver ?? "";
            txtEsmVersion.Text = _versions?.esm ?? "";
            txtControllerVersion.Text = _versions?.controller ?? "";
        }

        private void SetupSearchBox()
        {
            var searchPanel = new Panel
            {
                Location = new Point(0, 360),
                Size = new Size(742, 35)
            };

            var lblSearch = new Label
            {
                Text = "🔍 Поиск:",
                Location = new Point(12, 8),
                Size = new Size(50, 25)
            };

            txtSearchIp = new TextBox
            {
                Location = new Point(65, 6),
                Size = new Size(250, 23)
            };
            txtSearchIp.TextChanged += TxtSearchIp_TextChanged;

            btnSearchClear = new Button
            {
                Text = "Очистить",
                Location = new Point(325, 5),
                Size = new Size(80, 25)
            };
            btnSearchClear.Click += (s, e) => { txtSearchIp.Text = ""; };

            searchPanel.Controls.Add(lblSearch);
            searchPanel.Controls.Add(txtSearchIp);
            searchPanel.Controls.Add(btnSearchClear);

            btnAddIp.Location = new Point(12, 410);
            btnEditIp.Location = new Point(120, 410);
            btnDeleteIp.Location = new Point(230, 410);

            tabIps.Controls.Add(searchPanel);
        }

        private void TxtSearchIp_TextChanged(object sender, EventArgs e)
        {
            string searchText = txtSearchIp.Text.ToLower();

            if (string.IsNullOrWhiteSpace(searchText))
            {
                dataGridViewIps.DataSource = null;
                dataGridViewIps.DataSource = _ips;
            }
            else
            {
                var filtered = _ips.Where(ip =>
                    ip.Name.ToLower().Contains(searchText) ||
                    ip.Inn.ToLower().Contains(searchText) ||
                    ip.Token.ToLower().Contains(searchText)
                ).ToList();

                dataGridViewIps.DataSource = null;
                dataGridViewIps.DataSource = filtered;
            }
        }

        private void btnAddIp_Click(object sender, EventArgs e)
        {
            var form = new IpEditForm(null);
            if (form.ShowDialog() == DialogResult.OK)
            {
                _ips.Add(form.IpData);
                SaveIps();
                RefreshIpGrid();
            }
        }

        private void btnEditIp_Click(object sender, EventArgs e)
        {
            if (dataGridViewIps.CurrentRow?.DataBoundItem is IPData selected)
            {
                var form = new IpEditForm(selected);
                if (form.ShowDialog() == DialogResult.OK)
                {
                    SaveIps();
                    RefreshIpGrid();
                }
            }
        }

        private void btnDeleteIp_Click(object sender, EventArgs e)
        {
            if (dataGridViewIps.CurrentRow?.DataBoundItem is IPData selected)
            {
                if (MessageBox.Show($"Удалить {selected.Name}?", "Подтверждение", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    _ips.Remove(selected);
                    SaveIps();
                    RefreshIpGrid();
                }
            }
        }

        private void RefreshIpGrid()
        {
            dataGridViewIps.DataSource = null;
            dataGridViewIps.DataSource = _ips;
        }

        private void SaveIps()
        {
            ConfigManager.SaveIps(_ips);
        }

        private void btnSaveVersions_Click(object sender, EventArgs e)
        {
            _versions = new VersionsData
            {
                lm_module = txtLmVersion.Text.Trim(),
                atol_driver = txtAtolVersion.Text.Trim(),
                esm = txtEsmVersion.Text.Trim(),
                controller = txtControllerVersion.Text.Trim()
            };
            ConfigManager.SaveVersions(_versions);
            MessageBox.Show("Версии сохранены", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}