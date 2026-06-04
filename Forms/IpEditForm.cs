using HonestFlow.Models;
using System;
using System.Windows.Forms;

namespace HonestFlow
{
    public partial class IpEditForm : Form
    {
        public IPData IpData { get; private set; }

        public IpEditForm(IPData existing = null)
        {
            InitializeComponent();

            if (existing != null)
            {
                txtName.Text = existing.Name;
                txtPassword.Text = existing.Password;
                txtToken.Text = existing.Token;
                txtInn.Text = existing.Inn;
                cmbArchitecture.SelectedItem = existing.Architecture ?? "x64";
                IpData = existing;
            }
            else
            {
                IpData = new IPData();
                cmbArchitecture.SelectedIndex = 0;
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            // Валидация обязательных полей
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                MessageBox.Show("Заполните поле 'Имя ИП'!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(txtInn.Text))
            {
                MessageBox.Show("Заполните поле 'ИНН'!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(txtPassword.Text))
            {
                MessageBox.Show("Заполните поле 'Пароль'!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(txtToken.Text))
            {
                MessageBox.Show("Заполните поле 'Токен'!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Сохраняем данные
            IpData.Name = txtName.Text.Trim();
            IpData.Password = txtPassword.Text.Trim();
            IpData.Token = txtToken.Text.Trim();
            IpData.Inn = txtInn.Text.Trim();
            IpData.Architecture = cmbArchitecture.SelectedItem?.ToString() ?? "x64";

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}