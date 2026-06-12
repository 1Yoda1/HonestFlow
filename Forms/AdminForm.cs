using HonestFlow.Infrastructure;
using HonestFlow.Models;
using HonestFlow.Services.Core;
using HonestFlow.Services.Machine;
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HonestFlow
{
    public partial class AdminForm : Form
    {
        private readonly ISystemService _systemService;
        private readonly ILogService _logService;

        public AdminForm()
        {
            InitializeComponent();

            _logService = new LogService();
            _systemService = new SystemService(_logService);

            LoadSystemInfo();
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

            void Apply()
            {
                lblServiceStatus.Text = status switch
                {
                    "running" => "Статус: ✅ РАБОТАЕТ",
                    "stopped" => "Статус: ⏹️ ОСТАНОВЛЕНА",
                    "notfound" => "Статус: ❌ СЛУЖБА НЕ НАЙДЕНА",
                    _ => "Статус: ❓ НЕИЗВЕСТНО"
                };
            }

            if (lblServiceStatus.InvokeRequired)
                lblServiceStatus.Invoke((Action)Apply);
            else
                Apply();
        }

        private async Task CheckApi()
        {
            try
            {
                bool isAvailable = await _systemService.IsApiAvailable();
                if (isAvailable)
                {
                    var status = await _systemService.GetApiStatus();
                    MessageBox.Show($"API доступен!\n\nВерсия: {status?.Version}\nСтатус: {status?.Status}\nИНН: {status?.Inn ?? "не задан"}",
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

        private static void OpenLogsFolder()
        {
            string logFolder = Logger.GetLogsFolder();
            if (Directory.Exists(logFolder))
                System.Diagnostics.Process.Start("explorer.exe", logFolder);
            else
                MessageBox.Show("Папка с логами не найдена", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void LoadSystemInfo()
        {
            txtSystemInfo.Text = _systemService.GetSystemInfo();
        }
    }
}