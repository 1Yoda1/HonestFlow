using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using HonestFlow.Application.Diagnostics;
using HonestFlow.Application.Installation.Planning;
using HonestFlow.Application.Licensing;
using HonestFlow.Application.PointIdentity;
using HonestFlow.Application.PointStatus;

namespace HonestFlow.Infrastructure.Dialogs
{
    internal sealed class MainFormDialogService
    {
        private readonly IWin32Window _owner;
        private readonly IPointAddressService _pointAddressService;
        private readonly ILicenseObservationSnapshotStore _licenseSnapshotStore;

        public MainFormDialogService(
            IWin32Window owner,
            IPointAddressService pointAddressService,
            ILicenseObservationSnapshotStore licenseSnapshotStore)
        {
            _owner = owner;
            _pointAddressService = pointAddressService;
            _licenseSnapshotStore = licenseSnapshotStore;
        }

        public DiagnosticLogSelection ShowDiagnosticLogSelection()
        {
            var selected = ShowCheckedSelection(
                "Сбор диагностики",
                "Выберите, какие логи включить:",
                new[]
                {
                    new SelectionItem<string>("system", "Сведения о системе и статусы служб"),
                    new SelectionItem<string>("hf", "Логи HonestFlow"),
                    new SelectionItem<string>("lm", "Логи ЛМ ЧЗ"),
                    new SelectionItem<string>("esm", "Логи ЕСМ"),
                    new SelectionItem<string>("kkt", "Лог ККТ / АТОЛ")
                },
                checkAll: true);
            if (selected == null)
                return null;

            var keys = selected.Select(item => item.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var result = new DiagnosticLogSelection
            {
                IncludeSystemInfo = keys.Contains("system"),
                IncludeHonestFlow = keys.Contains("hf"),
                IncludeLm = keys.Contains("lm"),
                IncludeEsm = keys.Contains("esm"),
                IncludeKkt = keys.Contains("kkt")
            };
            return result.HasAnySelection ? result : null;
        }

        public IReadOnlyCollection<InstallationComponent> ShowComponentSelection()
        {
            var selected = ShowCheckedSelection(
                "Ручная переустановка",
                "Выберите компоненты для переустановки:",
                new[]
                {
                    new SelectionItem<InstallationComponent>(InstallationComponent.LmModule, "ЛМ ЧЗ"),
                    new SelectionItem<InstallationComponent>(InstallationComponent.AtolDriver, "Драйвер АТОЛ"),
                    new SelectionItem<InstallationComponent>(InstallationComponent.Esm, "ЕСМ"),
                    new SelectionItem<InstallationComponent>(InstallationComponent.Controller, "Контроллер ЛМ")
                },
                checkAll: false);

            return selected?.Select(item => item.Value).ToArray();
        }

        public MaintenanceAction? ShowMaintenanceAction(bool canReinstall, bool canRestoreDatabase)
        {
            using var form = CreateFixedDialog("Обслуживание точки", 420, 190);
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(12)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.Controls.Add(new Label
            {
                Text = "Выберите действие обслуживания:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            var reinstallButton = new Button
            {
                Text = "Переустановить компоненты",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 4),
                Enabled = canReinstall
            };
            var restoreButton = new Button
            {
                Text = "Восстановить базу ЛМ ЧЗ",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 4),
                Enabled = canRestoreDatabase
            };
            MaintenanceAction? selected = null;
            reinstallButton.Click += (_, _) =>
            {
                selected = MaintenanceAction.ReinstallComponents;
                form.DialogResult = DialogResult.OK;
            };
            restoreButton.Click += (_, _) =>
            {
                selected = MaintenanceAction.RestoreLmDatabase;
                form.DialogResult = DialogResult.OK;
            };
            layout.Controls.Add(reinstallButton, 0, 1);
            layout.Controls.Add(restoreButton, 0, 2);
            form.Controls.Add(layout);
            return form.ShowDialog(_owner) == DialogResult.OK ? selected : null;
        }

        public HelpRequestDialogResult ShowHelpRequest()
        {
            PointAddressResult pointAddress = ResolveCurrentPointAddress();
            bool addressFound = pointAddress.IsAvailable;
            using var form = CreateFixedDialog("Запросить помощь", 460, 390);
            form.ShowInTaskbar = false;
            var typeLabel = new Label { Text = "Тип проблемы:", Location = new Point(16, 18), AutoSize = true };
            var problemTypeBox = new ComboBox
            {
                Location = new Point(16, 42),
                Width = 428,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            problemTypeBox.Items.AddRange(new object[]
            {
                "Ошибка проверки кода маркировки", "Ошибка ККТ", "Ошибка Кассового ПО", "Другое"
            });
            problemTypeBox.SelectedIndex = 0;
            var addressLabel = new Label { Text = "Адрес точки:", Location = new Point(16, 82), AutoSize = true };
            var addressBox = new TextBox
            {
                Location = new Point(16, 106),
                Width = 428,
                MaxLength = PointAddressService.MaximumAddressLength,
                Text = pointAddress.Address ?? string.Empty
            };
            var addressHintLabel = new Label
            {
                Text = addressFound
                    ? "Адрес точки найден автоматически, проверьте его перед отправкой."
                    : "Адрес точки не указан. Введите его вручную, пожалуйста.",
                Location = new Point(16, 132),
                Size = new Size(428, 34),
                ForeColor = addressFound ? Color.FromArgb(71, 85, 105) : Color.FromArgb(180, 83, 9)
            };
            var messageLabel = new Label { Text = "Сообщение:", Location = new Point(16, 174), AutoSize = true };
            var messageBox = new TextBox
            {
                Location = new Point(16, 198), Width = 428, Height = 140,
                Multiline = true, ScrollBars = ScrollBars.Vertical
            };
            var okButton = new Button { Text = "Отправить", Location = new Point(254, 344), Width = 90 };
            var cancelButton = new Button
            {
                Text = "Отмена", DialogResult = DialogResult.Cancel,
                Location = new Point(354, 344), Width = 90
            };
            okButton.Click += (_, _) =>
            {
                string normalized = PointAddressService.NormalizeAddress(addressBox.Text);
                if (normalized == null)
                {
                    MessageBox.Show(
                        form,
                        $"Введите адрес торговой точки длиной не более {PointAddressService.MaximumAddressLength} символов.",
                        "Запросить помощь",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    addressBox.Focus();
                    return;
                }
                addressBox.Text = normalized;
                form.DialogResult = DialogResult.OK;
            };
            form.Controls.AddRange(new Control[]
            {
                typeLabel, problemTypeBox, addressLabel, addressBox, addressHintLabel,
                messageLabel, messageBox, okButton, cancelButton
            });
            form.AcceptButton = okButton;
            form.CancelButton = cancelButton;
            if (form.ShowDialog(_owner) != DialogResult.OK)
                return null;

            SavePointAddress(addressBox.Text);
            return new HelpRequestDialogResult(problemTypeBox.Text, addressBox.Text, messageBox.Text);
        }

        public PointAddressResult ResolveCurrentPointAddress() =>
            _pointAddressService.Resolve(_licenseSnapshotStore.Current);

        public PointAddressResult ResolvePointAddress(LicenseObservationSnapshot snapshot) =>
            _pointAddressService.Resolve(snapshot);

        public string ResolvePointAddressForOnlineAction(string title, string prompt)
        {
            PointAddressResult resolved = ResolveCurrentPointAddress();
            if (resolved.IsAvailable)
                return resolved.Address;

            using var form = CreateFixedDialog(title, 460, 160);
            form.ControlBox = false;
            form.ShowInTaskbar = false;
            var promptLabel = new Label { Text = prompt, Location = new Point(16, 16), Size = new Size(428, 36) };
            var addressBox = new TextBox
            {
                Location = new Point(16, 58), Width = 428,
                MaxLength = PointAddressService.MaximumAddressLength
            };
            var okButton = new Button { Text = "Продолжить", Location = new Point(244, 112), Width = 100 };
            okButton.Click += (_, _) =>
            {
                string normalized = PointAddressService.NormalizeAddress(addressBox.Text);
                if (normalized == null)
                {
                    MessageBox.Show(form, "Введите адрес торговой точки.", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    addressBox.Focus();
                    return;
                }
                addressBox.Text = normalized;
                form.DialogResult = DialogResult.OK;
            };
            form.Controls.AddRange(new Control[] { promptLabel, addressBox, okButton });
            form.AcceptButton = okButton;
            form.FormClosing += (_, args) =>
            {
                if (args.CloseReason == CloseReason.UserClosing && form.DialogResult != DialogResult.OK)
                    args.Cancel = true;
            };
            form.ShowDialog(_owner);
            SavePointAddress(addressBox.Text);
            return addressBox.Text;
        }

        public string ShowStartupRuDesktopPassword()
        {
            using var form = CreateFixedDialog("Настройка RuDesktop", 430, 180);
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.Controls.Add(new Label
            {
                Text = "RuDesktop установлен, но постоянный пароль ещё не настроен.\nВведите пароль точки, чтобы настроить доступ.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            layout.Controls.Add(new Label
            {
                Text = "Пароль точки", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft
            }, 0, 1);
            var passwordBox = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var okButton = new Button { Text = "Настроить", DialogResult = DialogResult.OK, Width = 100 };
            var cancelButton = new Button { Text = "Позже", DialogResult = DialogResult.Cancel, Width = 90 };
            buttons.Controls.Add(okButton);
            buttons.Controls.Add(cancelButton);
            layout.Controls.Add(passwordBox, 0, 2);
            layout.Controls.Add(buttons, 0, 3);
            form.Controls.Add(layout);
            form.AcceptButton = okButton;
            form.CancelButton = cancelButton;
            return form.ShowDialog(_owner) == DialogResult.OK ? passwordBox.Text : null;
        }

        public void ShowPointStatusReport(string report)
        {
            using var dialog = new Form
            {
                Text = "Подробнее — состояние точки",
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(820, 650),
                MinimumSize = new Size(680, 480),
                ShowIcon = false,
                ShowInTaskbar = false,
                MaximizeBox = true,
                MinimizeBox = false
            };
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10F),
                Text = report
            };
            var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Color.FromArgb(248, 250, 252) };
            header.Controls.Add(new Label
            {
                AutoSize = true, Font = new Font("Segoe UI Semibold", 14F),
                ForeColor = Color.FromArgb(30, 41, 59), Location = new Point(18, 12),
                Text = "Состояние всех узлов"
            });
            header.Controls.Add(new Label
            {
                AutoSize = true, Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(100, 116, 139), Location = new Point(20, 42),
                Text = "Исходные статусы, принятые решения и доступные действия"
            });
            var closeButton = new Button
            {
                Text = "Закрыть", DialogResult = DialogResult.OK, Dock = DockStyle.Right,
                Width = 120, FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White
            };
            closeButton.FlatAppearance.BorderSize = 0;
            var footer = new Panel
            {
                Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(0, 8, 0, 4),
                BackColor = Color.FromArgb(248, 250, 252)
            };
            footer.Controls.Add(closeButton);
            dialog.Controls.Add(textBox);
            dialog.Controls.Add(header);
            dialog.Controls.Add(footer);
            dialog.AcceptButton = closeButton;
            dialog.CancelButton = closeButton;
            dialog.ShowDialog(_owner);
        }

        public void ShowNodeDetails(NodeStatus status)
        {
            if (string.IsNullOrWhiteSpace(status?.Details))
                return;
            MessageBox.Show(
                _owner,
                status.Details,
                status.Level == NodeLevel.Error ? "Требуется внимание" : "Подробности проверки",
                MessageBoxButtons.OK,
                status.Level == NodeLevel.Error ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        public static string GetComponentDisplayName(InstallationComponent component) => component switch
        {
            InstallationComponent.LmModule => "ЛМ ЧЗ",
            InstallationComponent.AtolDriver => "Драйвер АТОЛ",
            InstallationComponent.Esm => "ЕСМ",
            InstallationComponent.Controller => "Контроллер ЛМ",
            _ => component.ToString()
        };

        private List<SelectionItem<T>> ShowCheckedSelection<T>(
            string title,
            string caption,
            SelectionItem<T>[] items,
            bool checkAll)
        {
            using var form = CreateFixedDialog(title, 420, 300);
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.Controls.Add(new Label
            {
                Text = caption, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            var checkedList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
            foreach (SelectionItem<T> item in items)
                checkedList.Items.Add(item, checkAll);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
            var cancelButton = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Width = 90 };
            buttons.Controls.Add(okButton);
            buttons.Controls.Add(cancelButton);
            layout.Controls.Add(checkedList, 0, 1);
            layout.Controls.Add(buttons, 0, 2);
            form.Controls.Add(layout);
            form.AcceptButton = okButton;
            form.CancelButton = cancelButton;
            return form.ShowDialog(_owner) == DialogResult.OK
                ? checkedList.CheckedItems.Cast<SelectionItem<T>>().ToList()
                : null;
        }

        private void SavePointAddress(string address) =>
            _pointAddressService.Save(
                _licenseSnapshotStore.Current?.DeviceId,
                address,
                PointAddressSource.Manual);

        private static Form CreateFixedDialog(string title, int width, int height) => new()
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(width, height)
        };

        private sealed class SelectionItem<T>
        {
            public SelectionItem(T value, string text) { Value = value; Text = text; }
            public T Value { get; }
            public string Text { get; }
            public override string ToString() => Text;
        }
    }

    internal enum MaintenanceAction
    {
        ReinstallComponents,
        RestoreLmDatabase
    }

    internal sealed class HelpRequestDialogResult
    {
        public HelpRequestDialogResult(string problemType, string fiscalAddress, string message)
        {
            ProblemType = problemType;
            FiscalAddress = fiscalAddress;
            Message = message;
        }
        public string ProblemType { get; }
        public string FiscalAddress { get; }
        public string Message { get; }
    }
}
