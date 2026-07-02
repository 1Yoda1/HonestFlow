using System;
using System.Windows.Forms;
using HonestFlow.Infrastructure;

namespace HonestFlow.Application.Core
{
    /// <summary>
    /// Реализация сервиса прогресса
    /// </summary>
    public class ProgressService : IProgressService
    {
        private readonly ProgressBar _progressBar;
        private readonly Label _statusLabel;

        public ProgressService(ProgressBar progressBar, Label statusLabel)
        {
            _progressBar = progressBar;
            _statusLabel = statusLabel;
        }

        public void SetProgress(int percent, string stepName)
        {
            if (_progressBar.InvokeRequired)
            {
                _progressBar.Invoke(new Action(() => SetProgress(percent, stepName)));
                return;
            }

            _progressBar.Value = percent;
            _statusLabel.Text = $"{stepName} ({percent}%)";
            _statusLabel.Visible = !string.IsNullOrEmpty(stepName);
        }
    }
}
