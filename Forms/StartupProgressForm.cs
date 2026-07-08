using System;
using System.Drawing;
using System.Windows.Forms;

namespace HonestFlow
{
    public class StartupProgressForm : Form
    {
        private readonly Label _titleLabel;
        private readonly Label _statusLabel;
        private readonly ProgressBar _progressBar;

        public StartupProgressForm()
        {
            _titleLabel = new Label();
            _statusLabel = new Label();
            _progressBar = new ProgressBar();

            SuspendLayout();

            BackColor = Color.FromArgb(243, 246, 250);
            ClientSize = new Size(460, 172);
            ControlBox = false;
            Font = new Font("Segoe UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = nameof(StartupProgressForm);
            ShowIcon = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "HonestFlow";

            _titleLabel.AutoSize = false;
            _titleLabel.Font = new Font("Segoe UI", 18F, FontStyle.Bold);
            _titleLabel.ForeColor = Color.FromArgb(20, 31, 51);
            _titleLabel.Location = new Point(26, 22);
            _titleLabel.Size = new Size(408, 42);
            _titleLabel.Text = "HonestFlow";
            _titleLabel.TextAlign = ContentAlignment.MiddleLeft;

            _statusLabel.AutoEllipsis = true;
            _statusLabel.Font = new Font("Segoe UI", 10F);
            _statusLabel.ForeColor = Color.FromArgb(51, 65, 85);
            _statusLabel.Location = new Point(28, 76);
            _statusLabel.Size = new Size(404, 28);
            _statusLabel.Text = "Подготовка к запуску...";
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;

            _progressBar.Location = new Point(30, 118);
            _progressBar.Maximum = 100;
            _progressBar.Minimum = 0;
            _progressBar.Size = new Size(400, 22);
            _progressBar.Style = ProgressBarStyle.Blocks;
            _progressBar.Value = 0;

            Controls.Add(_titleLabel);
            Controls.Add(_statusLabel);
            Controls.Add(_progressBar);

            ResumeLayout(false);
        }

        public void SetProgress(int percent, string status)
        {
            if (IsDisposed)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetProgress(percent, status)));
                return;
            }

            int safePercent = Math.Min(Math.Max(percent, _progressBar.Minimum), _progressBar.Maximum);
            _progressBar.Value = safePercent;
            _statusLabel.Text = string.IsNullOrWhiteSpace(status)
                ? "Подготовка к запуску..."
                : status;
        }
    }
}
