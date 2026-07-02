namespace HonestFlow
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
                components.Dispose();

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.rootLayout = new System.Windows.Forms.TableLayoutPanel();
            this.panelHeader = new System.Windows.Forms.Panel();
            this.labelTitle = new System.Windows.Forms.Label();
            this.lblHeaderStatus = new System.Windows.Forms.Label();

            this.mainLayout = new System.Windows.Forms.TableLayoutPanel();

            this.panelLeft = new System.Windows.Forms.Panel();
            this.leftLayout = new System.Windows.Forms.TableLayoutPanel();
            this.lblAuthTitle = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.button2 = new System.Windows.Forms.Button();
            this.btnCheckWithoutPassword = new System.Windows.Forms.Button();
            this.btnDiagnostics = new System.Windows.Forms.Button();
            this.btnOpenKktDriver = new System.Windows.Forms.Button();
            this.btnOpenEsm = new System.Windows.Forms.Button();
            this.btnDetails = new System.Windows.Forms.Button();

            this.panelNodes = new System.Windows.Forms.Panel();
            this.nodesLayout = new System.Windows.Forms.TableLayoutPanel();
            this.lblNodesTitle = new System.Windows.Forms.Label();
            this.nodeTable = new System.Windows.Forms.TableLayoutPanel();

            this.lblLmNode = new System.Windows.Forms.Label();
            this.lblLmStatusText = new System.Windows.Forms.Label();
            this.lblLmCircle = new System.Windows.Forms.Label();
            this.btnLmAction = new System.Windows.Forms.Button();

            this.lblControllerNode = new System.Windows.Forms.Label();
            this.lblControllerStatusText = new System.Windows.Forms.Label();
            this.lblControllerCircle = new System.Windows.Forms.Label();
            this.btnControllerAction = new System.Windows.Forms.Button();

            this.lblEsmNode = new System.Windows.Forms.Label();
            this.lblEsmStatusText = new System.Windows.Forms.Label();
            this.lblEsmCircle = new System.Windows.Forms.Label();
            this.btnEsmAction = new System.Windows.Forms.Button();

            this.lblKktNode = new System.Windows.Forms.Label();
            this.lblKktStatusText = new System.Windows.Forms.Label();
            this.lblKktCircle = new System.Windows.Forms.Label();
            this.btnKktAction = new System.Windows.Forms.Button();

            this.lblCloudNode = new System.Windows.Forms.Label();
            this.lblCloudStatusText = new System.Windows.Forms.Label();
            this.lblCloudCircle = new System.Windows.Forms.Label();
            this.btnCloudAction = new System.Windows.Forms.Button();

            this.panelBottom = new System.Windows.Forms.Panel();
            this.bottomLayout = new System.Windows.Forms.TableLayoutPanel();
            this.lblStatus = new System.Windows.Forms.Label();
            this.progressBar = new System.Windows.Forms.ProgressBar();

            this.listBox1 = new System.Windows.Forms.ListBox();
            this.button1 = new System.Windows.Forms.Button();
            this.buttonInstall = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();

            this.rootLayout.SuspendLayout();
            this.panelHeader.SuspendLayout();
            this.mainLayout.SuspendLayout();
            this.panelLeft.SuspendLayout();
            this.leftLayout.SuspendLayout();
            this.panelNodes.SuspendLayout();
            this.nodesLayout.SuspendLayout();
            this.nodeTable.SuspendLayout();
            this.panelBottom.SuspendLayout();
            this.bottomLayout.SuspendLayout();
            this.SuspendLayout();

            // rootLayout
            this.rootLayout.ColumnCount = 1;
            this.rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.Controls.Add(this.panelHeader, 0, 0);
            this.rootLayout.Controls.Add(this.mainLayout, 0, 1);
            this.rootLayout.Controls.Add(this.panelBottom, 0, 2);
            this.rootLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rootLayout.RowCount = 3;
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 64F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 74F));

            // panelHeader
            this.panelHeader.BackColor = System.Drawing.Color.FromArgb(20, 31, 51);
            this.panelHeader.Controls.Add(this.labelTitle);
            this.panelHeader.Controls.Add(this.lblHeaderStatus);
            this.panelHeader.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelHeader.Padding = new System.Windows.Forms.Padding(22, 0, 22, 0);

            // labelTitle
            this.labelTitle.Dock = System.Windows.Forms.DockStyle.Left;
            this.labelTitle.Font = new System.Drawing.Font("Segoe UI", 18F, System.Drawing.FontStyle.Bold);
            this.labelTitle.ForeColor = System.Drawing.Color.White;
            this.labelTitle.Size = new System.Drawing.Size(300, 64);
            this.labelTitle.Text = "HonestFlow";
            this.labelTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // lblHeaderStatus
            this.lblHeaderStatus.Dock = System.Windows.Forms.DockStyle.Right;
            this.lblHeaderStatus.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.lblHeaderStatus.ForeColor = System.Drawing.Color.FromArgb(251, 191, 36);
            this.lblHeaderStatus.Size = new System.Drawing.Size(330, 64);
            this.lblHeaderStatus.Text = "● Ожидание проверки";
            this.lblHeaderStatus.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            // mainLayout
            this.mainLayout.BackColor = System.Drawing.Color.FromArgb(243, 246, 250);
            this.mainLayout.ColumnCount = 2;
            this.mainLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 270F));
            this.mainLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.mainLayout.Controls.Add(this.panelLeft, 0, 0);
            this.mainLayout.Controls.Add(this.panelNodes, 1, 0);
            this.mainLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mainLayout.Padding = new System.Windows.Forms.Padding(14);

            // panelLeft
            this.panelLeft.BackColor = System.Drawing.Color.White;
            this.panelLeft.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panelLeft.Controls.Add(this.leftLayout);
            this.panelLeft.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelLeft.Margin = new System.Windows.Forms.Padding(0, 0, 10, 0);
            this.panelLeft.Padding = new System.Windows.Forms.Padding(14);

            // leftLayout
            this.leftLayout.ColumnCount = 1;
            this.leftLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.leftLayout.Controls.Add(this.lblAuthTitle, 0, 0);
            this.leftLayout.Controls.Add(this.label1, 0, 1);
            this.leftLayout.Controls.Add(this.textBox1, 0, 2);
            this.leftLayout.Controls.Add(this.button2, 0, 3);
            this.leftLayout.Controls.Add(this.btnCheckWithoutPassword, 0, 4);
            this.leftLayout.Controls.Add(this.btnDiagnostics, 0, 5);
            this.leftLayout.Controls.Add(this.btnOpenKktDriver, 0, 6);
            this.leftLayout.Controls.Add(this.btnOpenEsm, 0, 7);
            this.leftLayout.Controls.Add(this.btnDetails, 0, 8);
            this.leftLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.leftLayout.RowCount = 10;
            this.leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 38F));
            this.leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));

            // lblAuthTitle
            this.lblAuthTitle.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblAuthTitle.Font = new System.Drawing.Font("Segoe UI", 13F, System.Drawing.FontStyle.Bold);
            this.lblAuthTitle.ForeColor = System.Drawing.Color.FromArgb(20, 31, 51);
            this.lblAuthTitle.Text = "Авторизация";
            this.lblAuthTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // label1
            this.label1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.label1.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            this.label1.ForeColor = System.Drawing.Color.FromArgb(20, 31, 51);
            this.label1.Text = "Пароль доступа";
            this.label1.TextAlign = System.Drawing.ContentAlignment.BottomLeft;

            // textBox1
            this.textBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.textBox1.Font = new System.Drawing.Font("Segoe UI", 10.5F);
            this.textBox1.Margin = new System.Windows.Forms.Padding(0, 3, 0, 5);
            this.textBox1.UseSystemPasswordChar = true;

            // button2
            ConfigureButton(this.button2, "Войти и запустить", true);
            this.button2.Click += new System.EventHandler(this.Button2_Click);

            // btnCheckWithoutPassword
            ConfigureButton(this.btnCheckWithoutPassword, "Проверить без пароля", false);
            this.btnCheckWithoutPassword.Click += new System.EventHandler(this.BtnDiagnostics_Click);

            // btnDiagnostics
            ConfigureButton(this.btnDiagnostics, "Диагностика и ремонт", false);
            this.btnDiagnostics.Click += new System.EventHandler(this.BtnDiagnostics_Click);

            // btnOpenKktDriver
            ConfigureButton(this.btnOpenKktDriver, "Открыть Драйвер ККТ", false);

            // btnOpenEsm
            ConfigureButton(this.btnOpenEsm, "Открыть ЕСМ", false);

            // btnDetails
            ConfigureButton(this.btnDetails, "Журнал выполнения", false);
            this.btnDetails.Click += new System.EventHandler(this.BtnDetails_Click);

            // panelNodes
            this.panelNodes.BackColor = System.Drawing.Color.White;
            this.panelNodes.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panelNodes.Controls.Add(this.nodesLayout);
            this.panelNodes.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelNodes.Padding = new System.Windows.Forms.Padding(18, 8, 18, 18);

            // nodesLayout
            this.nodesLayout.ColumnCount = 1;
            this.nodesLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.nodesLayout.Controls.Add(this.lblNodesTitle, 0, 0);
            this.nodesLayout.Controls.Add(this.nodeTable, 0, 1);
            this.nodesLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.nodesLayout.RowCount = 2;
            this.nodesLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.nodesLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));

            // lblNodesTitle
            this.lblNodesTitle.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblNodesTitle.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            this.lblNodesTitle.ForeColor = System.Drawing.Color.FromArgb(20, 31, 51);
            this.lblNodesTitle.Text = "Состояние точки";
            this.lblNodesTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // nodeTable
            this.nodeTable.ColumnCount = 4;
            this.nodeTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 150F));
            this.nodeTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.nodeTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 64F));
            this.nodeTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 150F));
            this.nodeTable.Dock = System.Windows.Forms.DockStyle.Top;
            this.nodeTable.Height = 290;
            this.nodeTable.RowCount = 5;
            this.nodeTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 58F));
            this.nodeTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 58F));
            this.nodeTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 58F));
            this.nodeTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 58F));
            this.nodeTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 58F));

            ConfigureNodeRow(0, this.lblLmNode, this.lblLmStatusText, this.lblLmCircle, this.btnLmAction,
                "ЛМ ЧЗ", System.Drawing.Color.FromArgb(34, 197, 94), "Подробнее");

            ConfigureNodeRow(1, this.lblControllerNode, this.lblControllerStatusText, this.lblControllerCircle, this.btnControllerAction,
                "Контроллер", System.Drawing.Color.FromArgb(251, 191, 36), "Исправить");

            ConfigureNodeRow(2, this.lblEsmNode, this.lblEsmStatusText, this.lblEsmCircle, this.btnEsmAction,
                "ЕСМ", System.Drawing.Color.FromArgb(239, 68, 68), "Исправить");

            ConfigureNodeRow(3, this.lblKktNode, this.lblKktStatusText, this.lblKktCircle, this.btnKktAction,
                "ККТ", System.Drawing.Color.FromArgb(148, 163, 184), "Проверить");

            ConfigureNodeRow(4, this.lblCloudNode, this.lblCloudStatusText, this.lblCloudCircle, this.btnCloudAction,
                "Облако", System.Drawing.Color.FromArgb(34, 197, 94), "Подробнее");

            // panelBottom
            this.panelBottom.BackColor = System.Drawing.Color.FromArgb(243, 246, 250);
            this.panelBottom.Controls.Add(this.bottomLayout);
            this.panelBottom.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelBottom.Padding = new System.Windows.Forms.Padding(14, 0, 14, 10);

            // bottomLayout
            this.bottomLayout.BackColor = System.Drawing.Color.White;
            this.bottomLayout.ColumnCount = 1;
            this.bottomLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.bottomLayout.Controls.Add(this.lblStatus, 0, 0);
            this.bottomLayout.Controls.Add(this.progressBar, 0, 1);
            this.bottomLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.bottomLayout.Padding = new System.Windows.Forms.Padding(18, 10, 18, 10);
            this.bottomLayout.RowCount = 2;
            this.bottomLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.bottomLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));

            // lblStatus
            this.lblStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblStatus.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.lblStatus.ForeColor = System.Drawing.Color.FromArgb(51, 65, 85);
            this.lblStatus.Text = "Ожидание запуска проверки";
            this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // progressBar
            this.progressBar.Dock = System.Windows.Forms.DockStyle.Fill;
            this.progressBar.Margin = new System.Windows.Forms.Padding(0, 4, 0, 2);
            this.progressBar.Style = System.Windows.Forms.ProgressBarStyle.Blocks;

            // hidden legacy controls
            this.listBox1.Visible = false;
            this.button1.Visible = false;
            this.buttonInstall.Visible = false;
            this.label2.Visible = false;

            // MainForm
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(243, 246, 250);
            this.ClientSize = new System.Drawing.Size(860, 560);
            this.Controls.Add(this.rootLayout);
            this.Controls.Add(this.listBox1);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.buttonInstall);
            this.Controls.Add(this.label2);
            this.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimumSize = new System.Drawing.Size(860, 560);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "HonestFlow";

            this.rootLayout.ResumeLayout(false);
            this.panelHeader.ResumeLayout(false);
            this.mainLayout.ResumeLayout(false);
            this.panelLeft.ResumeLayout(false);
            this.leftLayout.ResumeLayout(false);
            this.leftLayout.PerformLayout();
            this.panelNodes.ResumeLayout(false);
            this.nodesLayout.ResumeLayout(false);
            this.nodeTable.ResumeLayout(false);
            this.nodeTable.PerformLayout();
            this.panelBottom.ResumeLayout(false);
            this.bottomLayout.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

       

        private System.Windows.Forms.TableLayoutPanel rootLayout;
        private System.Windows.Forms.Panel panelHeader;
        private System.Windows.Forms.Label labelTitle;
        private System.Windows.Forms.Label lblHeaderStatus;

        private System.Windows.Forms.TableLayoutPanel mainLayout;

        private System.Windows.Forms.Panel panelLeft;
        private System.Windows.Forms.TableLayoutPanel leftLayout;
        private System.Windows.Forms.Label lblAuthTitle;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.Button btnCheckWithoutPassword;
        private System.Windows.Forms.Button btnDiagnostics;
        private System.Windows.Forms.Button btnOpenKktDriver;
        private System.Windows.Forms.Button btnOpenEsm;
        private System.Windows.Forms.Button btnDetails;

        private System.Windows.Forms.Panel panelNodes;
        private System.Windows.Forms.TableLayoutPanel nodesLayout;
        private System.Windows.Forms.Label lblNodesTitle;
        private System.Windows.Forms.TableLayoutPanel nodeTable;

        private System.Windows.Forms.Label lblLmNode;
        private System.Windows.Forms.Label lblLmStatusText;
        private System.Windows.Forms.Label lblLmCircle;
        private System.Windows.Forms.Button btnLmAction;

        private System.Windows.Forms.Label lblControllerNode;
        private System.Windows.Forms.Label lblControllerStatusText;
        private System.Windows.Forms.Label lblControllerCircle;
        private System.Windows.Forms.Button btnControllerAction;

        private System.Windows.Forms.Label lblEsmNode;
        private System.Windows.Forms.Label lblEsmStatusText;
        private System.Windows.Forms.Label lblEsmCircle;
        private System.Windows.Forms.Button btnEsmAction;

        private System.Windows.Forms.Label lblKktNode;
        private System.Windows.Forms.Label lblKktStatusText;
        private System.Windows.Forms.Label lblKktCircle;
        private System.Windows.Forms.Button btnKktAction;

        private System.Windows.Forms.Label lblCloudNode;
        private System.Windows.Forms.Label lblCloudStatusText;
        private System.Windows.Forms.Label lblCloudCircle;
        private System.Windows.Forms.Button btnCloudAction;

        private System.Windows.Forms.Panel panelBottom;
        private System.Windows.Forms.TableLayoutPanel bottomLayout;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.ProgressBar progressBar;

        private System.Windows.Forms.ListBox listBox1;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Button buttonInstall;
        private System.Windows.Forms.Label label2;
    }
}
