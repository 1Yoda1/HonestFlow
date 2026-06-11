namespace HonestFlow
{
    partial class AdminForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabService = new System.Windows.Forms.TabPage();
            this.groupBoxService = new System.Windows.Forms.GroupBox();
            this.btnStopService = new System.Windows.Forms.Button();
            this.btnStartService = new System.Windows.Forms.Button();
            this.btnRestartService = new System.Windows.Forms.Button();
            this.lblServiceStatus = new System.Windows.Forms.Label();
            this.btnCheckApi = new System.Windows.Forms.Button();
            this.btnOpenLogs = new System.Windows.Forms.Button();
            this.tabSystemInfo = new System.Windows.Forms.TabPage();
            this.txtSystemInfo = new System.Windows.Forms.RichTextBox();
            this.btnRefreshSystem = new System.Windows.Forms.Button();
            this.btnCopySystemInfo = new System.Windows.Forms.Button();

            this.tabControl1.SuspendLayout();
            this.tabService.SuspendLayout();
            this.groupBoxService.SuspendLayout();
            this.tabSystemInfo.SuspendLayout();
            this.SuspendLayout();

            // tabControl1
            this.tabControl1.Controls.Add(this.tabService);
            this.tabControl1.Controls.Add(this.tabSystemInfo);
            this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl1.Location = new System.Drawing.Point(0, 0);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(750, 550);
            this.tabControl1.TabIndex = 0;

            // ========== ВКЛАДКА "СЕРВИС" ==========
            this.tabService.Controls.Add(this.groupBoxService);
            this.tabService.Controls.Add(this.btnCheckApi);
            this.tabService.Controls.Add(this.btnOpenLogs);
            this.tabService.Location = new System.Drawing.Point(4, 24);
            this.tabService.Name = "tabService";
            this.tabService.Size = new System.Drawing.Size(742, 522);
            this.tabService.Text = "⚙️ Сервис";
            this.tabService.UseVisualStyleBackColor = true;

            this.groupBoxService.Text = "Управление службой Regime";
            this.groupBoxService.Location = new System.Drawing.Point(20, 20);
            this.groupBoxService.Size = new System.Drawing.Size(450, 160);
            this.groupBoxService.TabIndex = 0;

            this.btnStopService.BackColor = System.Drawing.Color.FromArgb(231, 76, 60);
            this.btnStopService.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStopService.ForeColor = System.Drawing.Color.White;
            this.btnStopService.Location = new System.Drawing.Point(20, 35);
            this.btnStopService.Size = new System.Drawing.Size(130, 40);
            this.btnStopService.Text = "⏹️ Остановить";
            this.btnStopService.UseVisualStyleBackColor = false;
            this.btnStopService.Click += new System.EventHandler(this.BtnStopService_Click);

            this.btnStartService.BackColor = System.Drawing.Color.FromArgb(46, 204, 113);
            this.btnStartService.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStartService.ForeColor = System.Drawing.Color.White;
            this.btnStartService.Location = new System.Drawing.Point(160, 35);
            this.btnStartService.Size = new System.Drawing.Size(130, 40);
            this.btnStartService.Text = "▶️ Запустить";
            this.btnStartService.UseVisualStyleBackColor = false;
            this.btnStartService.Click += new System.EventHandler(this.BtnStartService_Click);

            this.btnRestartService.BackColor = System.Drawing.Color.FromArgb(243, 156, 18);
            this.btnRestartService.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRestartService.ForeColor = System.Drawing.Color.White;
            this.btnRestartService.Location = new System.Drawing.Point(300, 35);
            this.btnRestartService.Size = new System.Drawing.Size(130, 40);
            this.btnRestartService.Text = "🔄 Перезапустить";
            this.btnRestartService.UseVisualStyleBackColor = false;
            this.btnRestartService.Click += new System.EventHandler(this.BtnRestartService_Click);

            this.lblServiceStatus.AutoSize = true;
            this.lblServiceStatus.Location = new System.Drawing.Point(20, 95);
            this.lblServiceStatus.Size = new System.Drawing.Size(110, 15);
            this.lblServiceStatus.Text = "Статус: загрузка...";
            this.lblServiceStatus.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);

            this.groupBoxService.Controls.Add(this.btnStopService);
            this.groupBoxService.Controls.Add(this.btnStartService);
            this.groupBoxService.Controls.Add(this.btnRestartService);
            this.groupBoxService.Controls.Add(this.lblServiceStatus);

            this.btnCheckApi.BackColor = System.Drawing.Color.FromArgb(155, 89, 182);
            this.btnCheckApi.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCheckApi.ForeColor = System.Drawing.Color.White;
            this.btnCheckApi.Location = new System.Drawing.Point(20, 200);
            this.btnCheckApi.Size = new System.Drawing.Size(180, 40);
            this.btnCheckApi.Text = "🔌 Проверить API ЛМ";
            this.btnCheckApi.UseVisualStyleBackColor = false;
            this.btnCheckApi.Click += new System.EventHandler(this.BtnCheckApi_Click);

            this.btnOpenLogs.BackColor = System.Drawing.Color.FromArgb(52, 73, 94);
            this.btnOpenLogs.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOpenLogs.ForeColor = System.Drawing.Color.White;
            this.btnOpenLogs.Location = new System.Drawing.Point(210, 200);
            this.btnOpenLogs.Size = new System.Drawing.Size(180, 40);
            this.btnOpenLogs.Text = "📁 Открыть логи";
            this.btnOpenLogs.UseVisualStyleBackColor = false;
            this.btnOpenLogs.Click += new System.EventHandler(this.BtnOpenLogs_Click);

            // ========== ВКЛАДКА "СИСТЕМА" ==========
            this.tabSystemInfo.Controls.Add(this.txtSystemInfo);
            this.tabSystemInfo.Controls.Add(this.btnRefreshSystem);
            this.tabSystemInfo.Controls.Add(this.btnCopySystemInfo);
            this.tabSystemInfo.Location = new System.Drawing.Point(4, 24);
            this.tabSystemInfo.Name = "tabSystemInfo";
            this.tabSystemInfo.Size = new System.Drawing.Size(742, 522);
            this.tabSystemInfo.Text = "🖥️ Система";
            this.tabSystemInfo.UseVisualStyleBackColor = true;

            this.txtSystemInfo.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
            this.txtSystemInfo.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtSystemInfo.ForeColor = System.Drawing.Color.LightGreen;
            this.txtSystemInfo.Location = new System.Drawing.Point(20, 20);
            this.txtSystemInfo.ReadOnly = true;
            this.txtSystemInfo.Size = new System.Drawing.Size(700, 420);
            this.txtSystemInfo.TabIndex = 0;

            this.btnRefreshSystem.BackColor = System.Drawing.Color.FromArgb(41, 128, 185);
            this.btnRefreshSystem.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRefreshSystem.ForeColor = System.Drawing.Color.White;
            this.btnRefreshSystem.Location = new System.Drawing.Point(20, 455);
            this.btnRefreshSystem.Size = new System.Drawing.Size(120, 40);
            this.btnRefreshSystem.Text = "🔄 Обновить";
            this.btnRefreshSystem.UseVisualStyleBackColor = false;
            this.btnRefreshSystem.Click += new System.EventHandler(this.BtnRefreshSystem_Click);

            this.btnCopySystemInfo.BackColor = System.Drawing.Color.FromArgb(100, 100, 100);
            this.btnCopySystemInfo.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCopySystemInfo.ForeColor = System.Drawing.Color.White;
            this.btnCopySystemInfo.Location = new System.Drawing.Point(150, 455);
            this.btnCopySystemInfo.Size = new System.Drawing.Size(120, 40);
            this.btnCopySystemInfo.Text = "📋 Копировать";
            this.btnCopySystemInfo.UseVisualStyleBackColor = false;
            this.btnCopySystemInfo.Click += new System.EventHandler(this.BtnCopySystemInfo_Click);

            // ========== ОСНОВНАЯ ФОРМА ==========
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(750, 550);
            this.Controls.Add(this.tabControl1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "AdminForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "🔧 Диагностика HonestFlow";
            this.Icon = null;

            this.tabControl1.ResumeLayout(false);
            this.tabService.ResumeLayout(false);
            this.groupBoxService.ResumeLayout(false);
            this.groupBoxService.PerformLayout();
            this.tabSystemInfo.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabService;
        private System.Windows.Forms.GroupBox groupBoxService;
        private System.Windows.Forms.Button btnStopService;
        private System.Windows.Forms.Button btnStartService;
        private System.Windows.Forms.Button btnRestartService;
        private System.Windows.Forms.Label lblServiceStatus;
        private System.Windows.Forms.Button btnCheckApi;
        private System.Windows.Forms.Button btnOpenLogs;
        private System.Windows.Forms.TabPage tabSystemInfo;
        private System.Windows.Forms.RichTextBox txtSystemInfo;
        private System.Windows.Forms.Button btnRefreshSystem;
        private System.Windows.Forms.Button btnCopySystemInfo;
    }
}