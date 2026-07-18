using System.Drawing;
using System.Windows.Forms;
using HonestFlow.Application.Auth;
using HonestFlow.Application.Licensing;

namespace HonestFlow
{
    public sealed class LicenseCheckProgressForm : Form
    {
        private readonly Label _client = CreateStatusLabel("○ Определение клиента");
        private readonly Label _device = CreateStatusLabel("○ Получение DeviceId");
        private readonly Label _manifest = CreateStatusLabel("○ Проверка лицензии");
        private readonly Label _decision = CreateStatusLabel("○ Формирование решения");
        private readonly ProgressBar _progress = new ProgressBar();

        public LicenseCheckProgressForm()
        {
            Text = "Проверка доступа HonestFlow";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            ClientSize = new Size(500, 285);

            var title = new Label
            {
                Text = "Проверяем доступ к HonestFlow",
                Left = 24,
                Top = 20,
                Width = 450,
                Height = 32,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 31, 51)
            };
            var hint = new Label
            {
                Text = "Подождите, это обычно занимает несколько секунд.",
                Left = 25,
                Top = 54,
                Width = 450,
                Height = 24,
                ForeColor = Color.FromArgb(71, 85, 105)
            };

            _client.SetBounds(30, 92, 440, 27);
            _device.SetBounds(30, 123, 440, 27);
            _manifest.SetBounds(30, 154, 440, 27);
            _decision.SetBounds(30, 185, 440, 42);
            _progress.SetBounds(25, 240, 450, 18);
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 25;

            Controls.AddRange(new Control[]
            {
                title, hint, _client, _device, _manifest, _decision, _progress
            });
        }

        public void Report(LicenseAuthenticationProgress progress)
        {
            if (progress == null || IsDisposed)
                return;

            switch (progress.Stage)
            {
                case LicenseAuthenticationStage.CheckingPassword:
                    _client.Text = "… Проверяем пароль точки";
                    break;
                case LicenseAuthenticationStage.ClientResolved:
                    SetSuccess(_client, "Клиент определён: " + (progress.ClientName ?? "без имени"));
                    _device.Text = "… Получаем DeviceId";
                    break;
                case LicenseAuthenticationStage.CheckingDeviceAndLicense:
                    _manifest.Text = "… Читаем и проверяем лицензию";
                    break;
            }
        }

        public void Complete(LicenseAuthenticationResult result)
        {
            if (result?.Client == null)
                return;

            SetSuccess(_client, "Клиент определён: " + result.Client.Name);
            LicenseObservationSnapshot snapshot = result.LicenseSnapshot;
            if (snapshot == null)
            {
                SetFailure(_device, "DeviceId и лицензия не проверены");
                SetFailure(_manifest, "Нет результата проверки лицензии");
                SetFailure(_decision, "Доступ будет определён безопасной политикой");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(snapshot.DeviceId))
                    SetFailure(_device, "DeviceId недоступен");
                else
                    SetSuccess(_device, "DeviceId получен");

                string source = snapshot.ManifestSource?.ToString() ?? "не определён";
                SetSuccess(_manifest, "Лицензия обработана, источник: " + source);
                if (snapshot.Decision == LicenseDecision.Allowed)
                    SetSuccess(_decision, "Доступ разрешён");
                else
                    SetFailure(_decision, snapshot.Message ?? "Доступ ограничен лицензией");
            }

            _progress.Style = ProgressBarStyle.Blocks;
            _progress.Value = 100;
        }

        private static Label CreateStatusLabel(string text) => new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = Color.FromArgb(71, 85, 105),
            AutoEllipsis = true
        };

        private static void SetSuccess(Label label, string text)
        {
            label.Text = "✓ " + text;
            label.ForeColor = Color.FromArgb(22, 163, 74);
        }

        private static void SetFailure(Label label, string text)
        {
            label.Text = "⚠ " + text;
            label.ForeColor = Color.FromArgb(217, 119, 6);
        }
    }
}
