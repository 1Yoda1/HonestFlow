namespace HonestFlow
{
    partial class IpEditForm
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
            this.txtName = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.txtPassword = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.txtToken = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.txtInn = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.cmbArchitecture = new System.Windows.Forms.ComboBox();
            this.btnSave = new System.Windows.Forms.Button();
            this.SuspendLayout();

            this.label1.Location = new System.Drawing.Point(20, 20);
            this.label1.Size = new System.Drawing.Size(80, 23);
            this.label1.Text = "Имя ИП:";
            this.txtName.Location = new System.Drawing.Point(110, 17);
            this.txtName.Size = new System.Drawing.Size(250, 23);

            this.label2.Location = new System.Drawing.Point(20, 55);
            this.label2.Size = new System.Drawing.Size(80, 23);
            this.label2.Text = "Пароль:";
            this.txtPassword.Location = new System.Drawing.Point(110, 52);
            this.txtPassword.Size = new System.Drawing.Size(250, 23);

            this.label3.Location = new System.Drawing.Point(20, 90);
            this.label3.Size = new System.Drawing.Size(80, 23);
            this.label3.Text = "Токен:";
            this.txtToken.Location = new System.Drawing.Point(110, 87);
            this.txtToken.Size = new System.Drawing.Size(250, 23);

            this.label4.Location = new System.Drawing.Point(20, 125);
            this.label4.Size = new System.Drawing.Size(80, 23);
            this.label4.Text = "ИНН:";
            this.txtInn.Location = new System.Drawing.Point(110, 122);
            this.txtInn.Size = new System.Drawing.Size(250, 23);

            this.label5.Location = new System.Drawing.Point(20, 160);
            this.label5.Size = new System.Drawing.Size(80, 23);
            this.label5.Text = "Разрядность:";

            this.cmbArchitecture.Location = new System.Drawing.Point(110, 157);
            this.cmbArchitecture.Size = new System.Drawing.Size(100, 23);
            this.cmbArchitecture.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbArchitecture.Items.AddRange(new object[] { "x64", "x86" });
            this.cmbArchitecture.SelectedIndex = 0;

            this.btnSave.Location = new System.Drawing.Point(150, 200);
            this.btnSave.Size = new System.Drawing.Size(100, 30);
            this.btnSave.Text = "Сохранить";
            this.btnSave.Click += new System.EventHandler(this.BtnSave_Click);

            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(400, 260);
            this.Controls.Add(this.btnSave);
            this.Controls.Add(this.cmbArchitecture);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.txtInn);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.txtToken);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.txtPassword);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.txtName);
            this.Controls.Add(this.label1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "IpEditForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Редактирование ИП";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox txtName;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox txtPassword;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox txtToken;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox txtInn;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.ComboBox cmbArchitecture;
        private System.Windows.Forms.Button btnSave;
    }
}