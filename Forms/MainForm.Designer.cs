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
            rootLayout = new System.Windows.Forms.TableLayoutPanel();
            panelHeader = new System.Windows.Forms.Panel();
            labelTitle = new System.Windows.Forms.Label();
            lblHeaderStatus = new System.Windows.Forms.Label();
            mainLayout = new System.Windows.Forms.TableLayoutPanel();
            panelLeft = new System.Windows.Forms.Panel();
            leftLayout = new System.Windows.Forms.TableLayoutPanel();
            lblAuthTitle = new System.Windows.Forms.Label();
            label1 = new System.Windows.Forms.Label();
            textBox1 = new System.Windows.Forms.TextBox();
            button2 = new System.Windows.Forms.Button();
            btnCheckWithoutPassword = new System.Windows.Forms.Button();
            btnDiagnostics = new System.Windows.Forms.Button();
            btnDetails = new System.Windows.Forms.Button();
            panelNodes = new System.Windows.Forms.Panel();
            nodesLayout = new System.Windows.Forms.TableLayoutPanel();
            lblNodesTitle = new System.Windows.Forms.Label();
            nodeTable = new System.Windows.Forms.TableLayoutPanel();
            lblLmNode = new System.Windows.Forms.Label();
            lblLmCircle = new System.Windows.Forms.Label();
            btnLmAction = new System.Windows.Forms.Button();
            lblControllerNode = new System.Windows.Forms.Label();
            lblControllerCircle = new System.Windows.Forms.Label();
            btnControllerAction = new System.Windows.Forms.Button();
            lblEsmNode = new System.Windows.Forms.Label();
            lblEsmCircle = new System.Windows.Forms.Label();
            btnEsmAction = new System.Windows.Forms.Button();
            lblKktNode = new System.Windows.Forms.Label();
            lblKktCircle = new System.Windows.Forms.Label();
            btnKktAction = new System.Windows.Forms.Button();
            lblGithubNode = new System.Windows.Forms.Label();
            lblGithubCircle = new System.Windows.Forms.Label();
            btnGithubAction = new System.Windows.Forms.Button();
            panelBottom = new System.Windows.Forms.Panel();
            bottomLayout = new System.Windows.Forms.TableLayoutPanel();
            lblStatus = new System.Windows.Forms.Label();
            progressBar = new System.Windows.Forms.ProgressBar();
            listBox1 = new System.Windows.Forms.ListBox();
            button1 = new System.Windows.Forms.Button();
            buttonInstall = new System.Windows.Forms.Button();
            label2 = new System.Windows.Forms.Label();
            rootLayout.SuspendLayout();
            panelHeader.SuspendLayout();
            mainLayout.SuspendLayout();
            panelLeft.SuspendLayout();
            leftLayout.SuspendLayout();
            panelNodes.SuspendLayout();
            nodesLayout.SuspendLayout();
            nodeTable.SuspendLayout();
            panelBottom.SuspendLayout();
            bottomLayout.SuspendLayout();
            SuspendLayout();
            // rootLayout
            rootLayout.ColumnCount = 1;
            rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            rootLayout.Controls.Add(panelHeader, 0, 0);
            rootLayout.Controls.Add(mainLayout, 0, 1);
            rootLayout.Controls.Add(panelBottom, 0, 2);
            rootLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            rootLayout.RowCount = 3;
            rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 64F));
            rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 74F));
            // panelHeader
            panelHeader.BackColor = System.Drawing.Color.FromArgb(20, 31, 51);
            panelHeader.Controls.Add(labelTitle);
            panelHeader.Controls.Add(lblHeaderStatus);
            panelHeader.Dock = System.Windows.Forms.DockStyle.Fill;
            panelHeader.Padding = new System.Windows.Forms.Padding(22, 0, 22, 0);
            // labelTitle
            labelTitle.Dock = System.Windows.Forms.DockStyle.Left;
            labelTitle.Font = new System.Drawing.Font("Segoe UI", 18F, System.Drawing.FontStyle.Bold);
            labelTitle.ForeColor = System.Drawing.Color.White;
            labelTitle.Size = new System.Drawing.Size(300, 64);
            labelTitle.Text = "HonestFlow";
            labelTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // lblHeaderStatus
            lblHeaderStatus.Dock = System.Windows.Forms.DockStyle.Right;
            lblHeaderStatus.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            lblHeaderStatus.ForeColor = System.Drawing.Color.FromArgb(251, 191, 36);
            lblHeaderStatus.Size = new System.Drawing.Size(330, 64);
            lblHeaderStatus.Text = "● Ожидание проверки";
            lblHeaderStatus.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // mainLayout
            mainLayout.BackColor = System.Drawing.Color.FromArgb(243, 246, 250);
            mainLayout.ColumnCount = 2;
            mainLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 270F));
            mainLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            mainLayout.Controls.Add(panelLeft, 0, 0);
            mainLayout.Controls.Add(panelNodes, 1, 0);
            mainLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            mainLayout.Padding = new System.Windows.Forms.Padding(14);
            // panelLeft
            panelLeft.BackColor = System.Drawing.Color.White;
            panelLeft.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            panelLeft.Controls.Add(leftLayout);
            panelLeft.Dock = System.Windows.Forms.DockStyle.Fill;
            panelLeft.Margin = new System.Windows.Forms.Padding(0, 0, 10, 0);
            panelLeft.Padding = new System.Windows.Forms.Padding(18);
            // leftLayout
            leftLayout.ColumnCount = 1;
            leftLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            leftLayout.Controls.Add(lblAuthTitle, 0, 0);
            leftLayout.Controls.Add(label1, 0, 1);
            leftLayout.Controls.Add(textBox1, 0, 2);
            leftLayout.Controls.Add(button2, 0, 3);
            leftLayout.Controls.Add(btnCheckWithoutPassword, 0, 4);
            leftLayout.Controls.Add(btnDiagnostics, 0, 5);
            leftLayout.Controls.Add(btnDetails, 0, 6);
            leftLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            leftLayout.RowCount = 8;
            leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 42F));
            leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 28F));
            leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 38F));
            leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 52F));
            leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 52F));
            leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 52F));
            leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 52F));
            leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            // lblAuthTitle
            lblAuthTitle.Dock = System.Windows.Forms.DockStyle.Fill;
            lblAuthTitle.Font = new System.Drawing.Font("Segoe UI", 13F, System.Drawing.FontStyle.Bold);
            lblAuthTitle.ForeColor = System.Drawing.Color.FromArgb(20, 31, 51);
            lblAuthTitle.Text = "Авторизация";
            lblAuthTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // label1
            label1.Dock = System.Windows.Forms.DockStyle.Fill;
            label1.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            label1.ForeColor = System.Drawing.Color.FromArgb(20, 31, 51);
            label1.Text = "Пароль доступа";
            label1.TextAlign = System.Drawing.ContentAlignment.BottomLeft;
            // textBox1
            textBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            textBox1.Font = new System.Drawing.Font("Segoe UI", 10.5F);
            textBox1.Margin = new System.Windows.Forms.Padding(0, 3, 0, 5);
            textBox1.UseSystemPasswordChar = true;
            // button2
            ConfigureButton(button2, "Войти и запустить", true);
            button2.Click += Button2_Click;
            // btnCheckWithoutPassword
            ConfigureButton(btnCheckWithoutPassword, "Проверить без пароля", false);
            btnCheckWithoutPassword.Click += BtnDiagnostics_Click;
            // btnDiagnostics
            ConfigureButton(btnDiagnostics, "Диагностика и ремонт", false);
            btnDiagnostics.Click += BtnDiagnostics_Click;
            // btnDetails
            ConfigureButton(btnDetails, "Журнал выполнения", false);
            btnDetails.Click += BtnDetails_Click;
            // panelNodes
            panelNodes.BackColor = System.Drawing.Color.White;
            panelNodes.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            panelNodes.Controls.Add(nodesLayout);
            panelNodes.Dock = System.Windows.Forms.DockStyle.Fill;
            panelNodes.Padding = new System.Windows.Forms.Padding(18);
            // nodesLayout
            nodesLayout.ColumnCount = 1;
            nodesLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            nodesLayout.Controls.Add(lblNodesTitle, 0, 0);
            nodesLayout.Controls.Add(nodeTable, 0, 1);
            nodesLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            nodesLayout.RowCount = 2;
            nodesLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 42F));
            nodesLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            // lblNodesTitle
            lblNodesTitle.Dock = System.Windows.Forms.DockStyle.Fill;
            lblNodesTitle.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            lblNodesTitle.ForeColor = System.Drawing.Color.FromArgb(20, 31, 51);
            lblNodesTitle.Text = "Состояние точки";
            lblNodesTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // nodeTable
            nodeTable.ColumnCount = 3;
            nodeTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            nodeTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 90F));
            nodeTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 160F));
            nodeTable.Dock = System.Windows.Forms.DockStyle.Top;
            nodeTable.Height = 270;
            nodeTable.RowCount = 5;
            nodeTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 54F));
            nodeTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 54F));
            nodeTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 54F));
            nodeTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 54F));
            nodeTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 54F));
            ConfigureNodeRow(0, lblLmNode, lblLmCircle, btnLmAction, "ЛМ ЧЗ", System.Drawing.Color.FromArgb(34, 197, 94), "Подробнее");
            ConfigureNodeRow(1, lblControllerNode, lblControllerCircle, btnControllerAction, "Контроллер", System.Drawing.Color.FromArgb(251, 191, 36), "Исправить");
            ConfigureNodeRow(2, lblEsmNode, lblEsmCircle, btnEsmAction, "ЕСМ", System.Drawing.Color.FromArgb(239, 68, 68), "Исправить");
            ConfigureNodeRow(3, lblKktNode, lblKktCircle, btnKktAction, "ККТ", System.Drawing.Color.FromArgb(148, 163, 184), "Проверить");
            ConfigureNodeRow(4, lblGithubNode, lblGithubCircle, btnGithubAction, "GitHub", System.Drawing.Color.FromArgb(34, 197, 94), "Подробнее");
            // panelBottom
            panelBottom.BackColor = System.Drawing.Color.FromArgb(243, 246, 250);
            panelBottom.Controls.Add(bottomLayout);
            panelBottom.Dock = System.Windows.Forms.DockStyle.Fill;
            panelBottom.Padding = new System.Windows.Forms.Padding(14, 0, 14, 10);
            // bottomLayout
            bottomLayout.BackColor = System.Drawing.Color.White;
            bottomLayout.ColumnCount = 1;
            bottomLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            bottomLayout.Controls.Add(lblStatus, 0, 0);
            bottomLayout.Controls.Add(progressBar, 0, 1);
            bottomLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            bottomLayout.Padding = new System.Windows.Forms.Padding(18, 10, 18, 10);
            bottomLayout.RowCount = 2;
            bottomLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            bottomLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            // lblStatus
            lblStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            lblStatus.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            lblStatus.ForeColor = System.Drawing.Color.FromArgb(51, 65, 85);
            lblStatus.Text = "Ожидание запуска проверки";
            lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // progressBar
            progressBar.Dock = System.Windows.Forms.DockStyle.Fill;
            progressBar.Margin = new System.Windows.Forms.Padding(0, 4, 0, 2);
            progressBar.Style = System.Windows.Forms.ProgressBarStyle.Blocks;
            // hidden legacy controls
            listBox1.Visible = false;
            button1.Visible = false;
            buttonInstall.Visible = false;
            label2.Visible = false;
            // MainForm
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            BackColor = System.Drawing.Color.FromArgb(243, 246, 250);
            ClientSize = new System.Drawing.Size(860, 560);
            Controls.Add(rootLayout);
            Controls.Add(listBox1);
            Controls.Add(button1);
            Controls.Add(buttonInstall);
            Controls.Add(label2);
            Font = new System.Drawing.Font("Segoe UI", 9F);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimumSize = new System.Drawing.Size(860, 560);
            Name = "MainForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            Text = "HonestFlow";
            rootLayout.ResumeLayout(false);
            panelHeader.ResumeLayout(false);
            mainLayout.ResumeLayout(false);
            panelLeft.ResumeLayout(false);
            leftLayout.ResumeLayout(false);
            leftLayout.PerformLayout();
            panelNodes.ResumeLayout(false);
            nodesLayout.ResumeLayout(false);
            nodeTable.ResumeLayout(false);
            nodeTable.PerformLayout();
            panelBottom.ResumeLayout(false);
            bottomLayout.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
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
        private System.Windows.Forms.Button btnDetails;

        private System.Windows.Forms.Panel panelNodes;
        private System.Windows.Forms.TableLayoutPanel nodesLayout;
        private System.Windows.Forms.Label lblNodesTitle;
        private System.Windows.Forms.TableLayoutPanel nodeTable;

        private System.Windows.Forms.Label lblLmNode;
        private System.Windows.Forms.Label lblLmCircle;
        private System.Windows.Forms.Button btnLmAction;

        private System.Windows.Forms.Label lblControllerNode;
        private System.Windows.Forms.Label lblControllerCircle;
        private System.Windows.Forms.Button btnControllerAction;

        private System.Windows.Forms.Label lblEsmNode;
        private System.Windows.Forms.Label lblEsmCircle;
        private System.Windows.Forms.Button btnEsmAction;

        private System.Windows.Forms.Label lblKktNode;
        private System.Windows.Forms.Label lblKktCircle;
        private System.Windows.Forms.Button btnKktAction;

        private System.Windows.Forms.Label lblGithubNode;
        private System.Windows.Forms.Label lblGithubCircle;
        private System.Windows.Forms.Button btnGithubAction;

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
