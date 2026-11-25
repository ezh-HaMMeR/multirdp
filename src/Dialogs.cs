using System;
using System.Drawing;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace MultiRdp
{
    internal static class Ui
    {
        public static readonly Color Ink = Color.FromArgb(25, 45, 70);
        public static readonly Color Background = Color.FromArgb(244, 247, 251);

        public static Icon LoadIcon(Size size)
        {
            using (var stream = typeof(Ui).Assembly.GetManifestResourceStream("MultiRdp.ico"))
                return new Icon(stream, size);
        }

        public static Button Button(string text, int width, bool primary = false)
        {
            var button = new Button { Text = text, Width = width, Height = 34, AutoSize = true };
            if (primary)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 0;
                button.BackColor = Color.FromArgb(28, 93, 177);
                button.ForeColor = Color.White;
            }
            return button;
        }

        public static void Error(IWin32Window owner, string message)
        {
            MessageBox.Show(owner, message, "MultiRDP", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public static void Dialog(Form form, string title, Size size, Icon icon)
        {
            form.Text = title;
            form.Icon = icon;
            form.Font = new Font("Segoe UI", 10);
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.ClientSize = size;
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.StartPosition = FormStartPosition.CenterParent;
            form.MinimizeBox = false;
            form.MaximizeBox = false;
            form.ShowInTaskbar = false;
            form.BackColor = Background;
            form.ForeColor = Ink;
        }
    }

    internal sealed class ProfileDialog : Form
    {
        private readonly TextBox name = new TextBox { MaxLength = 100 };
        private readonly TextBox address = new TextBox { MaxLength = 300 };
        private readonly TextBox user = new TextBox { MaxLength = 256 };
        private readonly TextBox password = new TextBox { UseSystemPasswordChar = true, MaxLength = 256 };
        private readonly CheckBox smartCards = new CheckBox { Text = "Смарт-карты и Windows Hello для бизнеса", AutoSize = true };
        private readonly CheckBox webAuthn = new CheckBox { Text = "WebAuthn (ключи доступа и безопасности)", AutoSize = true };
        private readonly CheckBox drives = new CheckBox { Text = "Диски", AutoSize = true };
        private readonly CheckBox clipboard = new CheckBox { Text = "Буфер обмена", AutoSize = true };
        private readonly CheckBox printers = new CheckBox { Text = "Принтеры", AutoSize = true };
        private readonly CheckBox scaleRemoteDesktop = new CheckBox
        {
            Text = "Масштабировать содержимое удаленного рабочего стола",
            AutoSize = true, Anchor = AnchorStyles.Left
        };
        private readonly Guid id;

        public Profile Result { get; private set; }

        public ProfileDialog(Profile source, Icon icon)
        {
            Ui.Dialog(this, source == null ? "Новое подключение" : "Изменить подключение", new Size(508, 483), icon);
            id = source == null ? Guid.NewGuid() : source.Id;

            var fields = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 12),
                ColumnCount = 2, RowCount = 8
            };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddField(fields, 0, "Название", name);
            AddField(fields, 1, "Сервер", address);
            AddField(fields, 2, "Пользователь", user);
            AddField(fields, 3, "Пароль", password);
            address.AccessibleDescription = "Имя сервера или IP-адрес. Порт указывается после двоеточия.";
            user.AccessibleDescription = @"Например, DOMAIN\user или user@example.com";
            var hint = new Label
            {
                Text = "Пустой пароль — запрос при подключении.",
                Dock = DockStyle.Fill, ForeColor = Color.FromArgb(77, 95, 116), Margin = new Padding(3, 0, 0, 0)
            };
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            fields.Controls.Add(hint, 1, 4);

            var access = new GroupBox { Text = "Доступ к локальным ресурсам", Dock = DockStyle.Fill, Padding = new Padding(11, 6, 8, 6) };
            var checks = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            checks.Controls.AddRange(new Control[] { smartCards, webAuthn, drives, clipboard, printers });
            access.Controls.Add(checks);
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            fields.Controls.Add(access, 0, 5);
            fields.SetColumnSpan(access, 2);

            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            fields.Controls.Add(scaleRemoteDesktop, 0, 6);
            fields.SetColumnSpan(scaleRemoteDesktop, 2);
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var cancel = Ui.Button("Отмена", 96);
            cancel.DialogResult = DialogResult.Cancel;
            var save = Ui.Button("Сохранить", 112, true);
            save.Click += delegate { SaveProfile(); };
            buttons.Controls.AddRange(new Control[] { cancel, save });
            fields.Controls.Add(buttons, 0, 7);
            fields.SetColumnSpan(buttons, 2);
            Controls.Add(fields);
            AcceptButton = save;
            CancelButton = cancel;

            smartCards.Checked = source == null || source.SmartCards;
            webAuthn.Checked = source == null || source.WebAuthn;
            drives.Checked = source == null || source.Drives;
            clipboard.Checked = source == null || source.Clipboard;
            printers.Checked = source == null || source.Printers;
            scaleRemoteDesktop.Checked = source != null && source.ScaleRemoteDesktop;

            if (source != null)
            {
                name.Text = source.Name;
                address.Text = source.Address;
                user.Text = source.UserName;
                try { password.Text = source.ReadPassword(); }
                catch (CryptographicException) { ShowPasswordError(); }
                catch (FormatException) { ShowPasswordError(); }
            }
        }

        private void ShowPasswordError()
        {
            Shown += delegate { Ui.Error(this, "Сохранённый пароль недоступен. Введите его заново или оставьте поле пустым."); };
        }

        private static void AddField(TableLayoutPanel panel, int row, string label, TextBox input)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            panel.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
            input.AccessibleName = label;
            input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            panel.Controls.Add(input, 1, row);
        }

        private void SaveProfile()
        {
            if (string.IsNullOrWhiteSpace(name.Text))
            {
                Ui.Error(this, "Введите название подключения.");
                name.Focus();
                return;
            }
            if (!RdpClient.ValidAddress(address.Text.Trim()))
            {
                Ui.Error(this, "Введите адрес сервера, например server.example.com или 192.168.1.10:3389.");
                address.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(user.Text))
            {
                Ui.Error(this, "Введите имя пользователя.");
                user.Focus();
                return;
            }

            try
            {
                Result = new Profile
                {
                    Id = id, Name = name.Text.Trim(), Address = address.Text.Trim(),
                    UserName = user.Text.Trim(), ProtectedPassword = Profile.ProtectPassword(password.Text),
                    SmartCards = smartCards.Checked, WebAuthn = webAuthn.Checked, Drives = drives.Checked,
                    Clipboard = clipboard.Checked, Printers = printers.Checked,
                    ScaleRemoteDesktop = scaleRemoteDesktop.Checked
                };
            }
            catch (CryptographicException)
            {
                Ui.Error(this, "Windows не смогла зашифровать пароль. Профиль не сохранён.");
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }

    internal sealed class SettingsDialog : Form
    {
        public SettingsDialog(ProfileStore store, Icon icon)
        {
            Ui.Dialog(this, "Настройки", new Size(405, 151), icon);
            var startup = new CheckBox { Text = "Запускать вместе с Windows", AutoSize = true };
            try { startup.Checked = Startup.Enabled; }
            catch (System.Security.SecurityException) { DisableStartupOption(startup); }
            catch (UnauthorizedAccessException) { DisableStartupOption(startup); }
            var hidden = new CheckBox { Text = "Запускать в свёрнутом виде", AutoSize = true, Checked = store.StartHidden };
            var close = Ui.Button("Готово", 92);
            close.DialogResult = DialogResult.OK;
            close.Margin = new Padding(3, 12, 3, 3);
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 10),
                FlowDirection = FlowDirection.TopDown, WrapContents = false
            };
            panel.Controls.AddRange(new Control[] { startup, hidden, close });
            Controls.Add(panel);
            AcceptButton = close;
            CancelButton = close;
            startup.CheckedChanged += delegate
            {
                try { Startup.Enabled = startup.Checked; }
                catch (Exception error)
                {
                    Ui.Error(this, "Не удалось изменить автозапуск. " + error.Message);
                    DisableStartupOption(startup);
                }
            };
            hidden.CheckedChanged += delegate
            {
                if (store.StartHidden == hidden.Checked)
                    return;
                store.StartHidden = hidden.Checked;
                try { store.Save(); }
                catch (Exception error)
                {
                    store.StartHidden = !hidden.Checked;
                    hidden.Checked = store.StartHidden;
                    Ui.Error(this, "Не удалось сохранить настройку. " + error.Message);
                }
            };
        }

        private static void DisableStartupOption(CheckBox checkbox)
        {
            checkbox.Text = "Автозапуск недоступен";
            checkbox.Enabled = false;
        }
    }
}
