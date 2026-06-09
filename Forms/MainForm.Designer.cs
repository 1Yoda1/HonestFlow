
namespace HonestFlow

{
    partial class MainForm
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
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.button2 = new System.Windows.Forms.Button();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.buttonInstall = new System.Windows.Forms.Button();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.btnDetails = new System.Windows.Forms.Button();
            this.panelHeader = new System.Windows.Forms.Panel();
            this.labelTitle = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            this.SuspendLayout();

            // ========== PANEL HEADER ==========
            this.panelHeader.BackColor = System.Drawing.Color.FromArgb(32, 45, 70);
            this.panelHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelHeader.Height = 70;
            this.panelHeader.Padding = new System.Windows.Forms.Padding(20, 10, 20, 10);

            this.labelTitle.AutoSize = true;
            this.labelTitle.Font = new System.Drawing.Font("Segoe UI", 18F, System.Drawing.FontStyle.Bold);
            this.labelTitle.ForeColor = System.Drawing.Color.White;
            this.labelTitle.Location = new System.Drawing.Point(20, 18);
            this.labelTitle.Text = "Установщик ТС ПИоТ";
            this.panelHeader.Controls.Add(this.labelTitle);

            // ========== LABEL1 ==========
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this.label1.ForeColor = System.Drawing.Color.FromArgb(32, 45, 70);
            this.label1.Location = new System.Drawing.Point(30, 95);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(133, 21);
            this.label1.TabIndex = 2;
            this.label1.Text = "Выберите ваше ИП";

            // ========== LABEL2 ==========
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.label2.ForeColor = System.Drawing.Color.FromArgb(64, 64, 64);
            this.label2.Location = new System.Drawing.Point(30, 270);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(0, 19);
            this.label2.TabIndex = 3;

            // ========== TEXTBOX1 (Пароль) ==========
            this.textBox1.Font = new System.Drawing.Font("Segoe UI", 12F);
            this.textBox1.Location = new System.Drawing.Point(30, 290);
            this.textBox1.Name = "textBox1";
            this.textBox1.Size = new System.Drawing.Size(240, 29);
            this.textBox1.TabIndex = 5;
            this.textBox1.UseSystemPasswordChar = true;

            // ========== BUTTON2 (Войти) ==========
            this.button2.BackColor = System.Drawing.Color.FromArgb(46, 204, 113);
            this.button2.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button2.FlatAppearance.BorderSize = 0;
            this.button2.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold);
            this.button2.ForeColor = System.Drawing.Color.White;
            this.button2.Location = new System.Drawing.Point(290, 285);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(140, 40);
            this.button2.TabIndex = 4;
            this.button2.Text = "Войти";
            this.button2.UseVisualStyleBackColor = false;
            this.button2.Click += new System.EventHandler(this.Button2_Click);

            // ========== BUTTON INSTALL ==========
            this.buttonInstall.BackColor = System.Drawing.Color.FromArgb(41, 128, 185);
            this.buttonInstall.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.buttonInstall.FlatAppearance.BorderSize = 0;
            this.buttonInstall.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this.buttonInstall.ForeColor = System.Drawing.Color.White;
            this.buttonInstall.Location = new System.Drawing.Point(30, 360);
            this.buttonInstall.Name = "buttonInstall";
            this.buttonInstall.Size = new System.Drawing.Size(400, 45);
            this.buttonInstall.TabIndex = 6;
            this.buttonInstall.Text = "НАЧАТЬ УСТАНОВКУ";
            this.buttonInstall.UseVisualStyleBackColor = false;
            this.buttonInstall.Visible = false;

            // ========== PROGRESS BAR ==========
            this.progressBar.Location = new System.Drawing.Point(30, 420);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(400, 10);
            this.progressBar.Style = System.Windows.Forms.ProgressBarStyle.Blocks;
            this.progressBar.TabIndex = 7;
            this.progressBar.Visible = false;

            // ========== LABEL STATUS ==========
            this.lblStatus.AutoSize = true;
            this.lblStatus.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblStatus.ForeColor = System.Drawing.Color.FromArgb(64, 64, 64);
            this.lblStatus.Location = new System.Drawing.Point(30, 440);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(0, 15);
            this.lblStatus.TabIndex = 10;

            // ========== BUTTON DETAILS ==========
            this.btnDetails.BackColor = System.Drawing.Color.FromArgb(100, 100, 100);
            this.btnDetails.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDetails.FlatAppearance.BorderSize = 0;
            this.btnDetails.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.btnDetails.ForeColor = System.Drawing.Color.White;
            this.btnDetails.Location = new System.Drawing.Point(310, 435);
            this.btnDetails.Name = "btnDetails";
            this.btnDetails.Size = new System.Drawing.Size(120, 30);
            this.btnDetails.TabIndex = 9;
            this.btnDetails.Text = "📋 Подробнее";
            this.btnDetails.UseVisualStyleBackColor = false;
            this.btnDetails.Click += new System.EventHandler(this.BtnDetails_Click);
            this.btnDetails.Visible = false;

            // ========== FORM ==========
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(480, 500);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.btnDetails);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.buttonInstall);
            this.Controls.Add(this.textBox1);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.panelHeader);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Установщик ТС ПИоТ";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.Button buttonInstall;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Button btnDetails;
        private System.Windows.Forms.Panel panelHeader;
        private System.Windows.Forms.Label labelTitle;
        private System.Windows.Forms.Label lblStatus;
    }
}