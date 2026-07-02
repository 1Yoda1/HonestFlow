using HonestFlow.Application.Core;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace HonestFlow.Application.Ui
{
    public sealed class WindowIconService
    {
        private readonly ILogService _log;

        public WindowIconService(ILogService log)
        {
            _log = log;
        }

        public void ApplyExecutableIcon(Form form)
        {
            try
            {
                using var icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
                if (icon != null)
                    form.Icon = (Icon)icon.Clone();
            }
            catch (Exception ex)
            {
                _log?.LogDebug($"Не удалось установить иконку окна: {ex.Message}");
            }
        }
    }
}
