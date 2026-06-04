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
            this.tabIps = new System.Windows.Forms.TabPage();
            this.dataGridViewIps = new System.Windows.Forms.DataGridView();
            this.btnAddIp = new System.Windows.Forms.Button();
            this.btnEditIp = new System.Windows.Forms.Button();
            this.btnDeleteIp = new System.Windows.Forms.Button();
            this.tabVersions = new System.Windows.Forms.TabPage();
            this.txtLmVersion = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.txtAtolVersion = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.txtEsmVersion = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.txtControllerVersion = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.btnSaveVersions = new System.Windows.Forms.Button();
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
            this.tabIps.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewIps)).BeginInit();
            this.tabVersions.SuspendLayout();
            this.tabService.SuspendLayout();
            this.groupBoxService.SuspendLayout();
            this.tabSystemInfo.SuspendLayout();
            this.SuspendLayout();

            // tabControl1
            this.tabControl1.Controls.Add(this.tabIps);
            this.tabControl1.Controls.Add(this.tabVersions);
            this.tabControl1.Controls.Add(this.tabService);
            this.tabControl1.Controls.Add(this.tabSystemInfo);
            this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl1.Location = new System.Drawing.Point(0, 0);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(750, 550);
            this.tabControl1.TabIndex = 0;

            // ========== ВКЛАДКА "ИП" ==========
            this.tabIps.Controls.Add(this.dataGridViewIps);
            this.tabIps.Controls.Add(this.btnAddIp);
            this.tabIps.Controls.Add(this.btnEditIp);
            this.tabIps.Controls.Add(this.btnDeleteIp);
            this.tabIps.Location = new System.Drawing.Point(4, 24);
            this.tabIps.Name = "tabIps";
            this.tabIps.Size = new System.Drawing.Size(742, 522);
            this.tabIps.Text = "📋 ИП";
            this.tabIps.UseVisualStyleBackColor = true;

            this.dataGridViewIps.Dock = System.Windows.Forms.DockStyle.Top;
            this.dataGridViewIps.Location = new System.Drawing.Point(0, 0);
            this.dataGridViewIps.Size = new System.Drawing.Size(742, 350);
            this.dataGridViewIps.TabIndex = 0;
            this.dataGridViewIps.AllowUserToAddRows = false;
            this.dataGridViewIps.AllowUserToDeleteRows = false;
            this.dataGridViewIps.AllowUserToOrderColumns = true;
            this.dataGridViewIps.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;

            this.btnAddIp.Location = new System.Drawing.Point(12, 370);
            this.btnAddIp.Size = new System.Drawing.Size(100, 35);
            this.btnAddIp.Text = "➕ Добавить";
            this.btnAddIp.UseVisualStyleBackColor = true;
            this.btnAddIp.Click += new System.EventHandler(this.BtnAddIp_Click);

            this.btnEditIp.Location = new System.Drawing.Point(120, 370);
            this.btnEditIp.Size = new System.Drawing.Size(100, 35);
            this.btnEditIp.Text = "✏️ Редактировать";
            this.btnEditIp.UseVisualStyleBackColor = true;
            this.btnEditIp.Click += new System.EventHandler(this.BtnEditIp_Click);

            this.btnDeleteIp.Location = new System.Drawing.Point(230, 370);
            this.btnDeleteIp.Size = new System.Drawing.Size(100, 35);
            this.btnDeleteIp.Text = "🗑️ Удалить";
            this.btnDeleteIp.UseVisualStyleBackColor = true;
            this.btnDeleteIp.Click += new System.EventHandler(this.BtnDeleteIp_Click);

            // ========== ВКЛАДКА "ВЕРСИИ" ==========
            this.tabVersions.Controls.Add(this.txtLmVersion);
            this.tabVersions.Controls.Add(this.label1);
            this.tabVersions.Controls.Add(this.txtAtolVersion);
            this.tabVersions.Controls.Add(this.label2);
            this.tabVersions.Controls.Add(this.txtEsmVersion);
            this.tabVersions.Controls.Add(this.label3);
            this.tabVersions.Controls.Add(this.txtControllerVersion);
            this.tabVersions.Controls.Add(this.label4);
            this.tabVersions.Controls.Add(this.btnSaveVersions);
            this.tabVersions.Location = new System.Drawing.Point(4, 24);
            this.tabVersions.Name = "tabVersions";
            this.tabVersions.Size = new System.Drawing.Size(742, 522);
            this.tabVersions.Text = "📦 Версии";
            this.tabVersions.UseVisualStyleBackColor = true;

            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(30, 35);
            this.label1.Size = new System.Drawing.Size(110, 15);
            this.label1.Text = "Локальный модуль ЧЗ:";

            this.txtLmVersion.Location = new System.Drawing.Point(170, 32);
            this.txtLmVersion.Size = new System.Drawing.Size(300, 23);
            this.txtLmVersion.Font = new System.Drawing.Font("Consolas", 9F);

            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(30, 75);
            this.label2.Size = new System.Drawing.Size(90, 15);
            this.label2.Text = "Драйвер АТОЛ:";

            this.txtAtolVersion.Location = new System.Drawing.Point(170, 72);
            this.txtAtolVersion.Size = new System.Drawing.Size(300, 23);
            this.txtAtolVersion.Font = new System.Drawing.Font("Consolas", 9F);

            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(30, 115);
            this.label3.Size = new System.Drawing.Size(35, 15);
            this.label3.Text = "ЕСМ:";

            this.txtEsmVersion.Location = new System.Drawing.Point(170, 112);
            this.txtEsmVersion.Size = new System.Drawing.Size(300, 23);
            this.txtEsmVersion.Font = new System.Drawing.Font("Consolas", 9F);

            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(30, 155);
            this.label4.Size = new System.Drawing.Size(75, 15);
            this.label4.Text = "Контроллер:";

            this.txtControllerVersion.Location = new System.Drawing.Point(170, 152);
            this.txtControllerVersion.Size = new System.Drawing.Size(300, 23);
            this.txtControllerVersion.Font = new System.Drawing.Font("Consolas", 9F);

            this.btnSaveVersions.Location = new System.Drawing.Point(170, 200);
            this.btnSaveVersions.Size = new System.Drawing.Size(120, 35);
            this.btnSaveVersions.Text = "💾 Сохранить";
            this.btnSaveVersions.UseVisualStyleBackColor = true;
            this.btnSaveVersions.Click += new System.EventHandler(this.BtnSaveVersions_Click);

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
            this.Text = "🔧 Админ-панель HonestFlow";
            this.Icon = null;

            this.tabControl1.ResumeLayout(false);
            this.tabIps.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewIps)).EndInit();
            this.tabVersions.ResumeLayout(false);
            this.tabVersions.PerformLayout();
            this.tabService.ResumeLayout(false);
            this.groupBoxService.ResumeLayout(false);
            this.groupBoxService.PerformLayout();
            this.tabSystemInfo.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        // ========== ОБЪЯВЛЕНИЯ КОМПОНЕНТОВ ==========
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabIps;
        private System.Windows.Forms.DataGridView dataGridViewIps;
        private System.Windows.Forms.Button btnAddIp;
        private System.Windows.Forms.Button btnEditIp;
        private System.Windows.Forms.Button btnDeleteIp;
        private System.Windows.Forms.TabPage tabVersions;
        private System.Windows.Forms.TextBox txtLmVersion;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox txtAtolVersion;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox txtEsmVersion;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox txtControllerVersion;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Button btnSaveVersions;
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