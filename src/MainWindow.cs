using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace MultiRdp
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 1 && args[0] == RdpClient.SessionArgument)
            {
                try { RdpClient.RunSession(); }
                catch (Exception error) { Ui.Error(null, "Не удалось открыть подключение. " + error.Message); }
                return;
            }

            using (var activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\MultiRdp.Activate"))
            using (var instance = new Mutex(false, @"Local\MultiRdp.Instance"))
            {
                bool ownsInstance;
                try { ownsInstance = instance.WaitOne(0); }
                catch (AbandonedMutexException) { ownsInstance = true; }
                if (!ownsInstance)
                {
                    activate.Set();
                    return;
                }

                try
                {
                    var store = ProfileStore.Load();
                    using (var icon = Ui.LoadIcon(SystemInformation.IconSize))
                    using (var trayIcon = Ui.LoadIcon(SystemInformation.SmallIconSize))
                    using (var window = new MainWindow(store, activate, icon, trayIcon))
                        RunMainWindow(window, store.StartHidden);
                }
                catch (Exception error)
                {
                    Ui.Error(null, "Не удалось открыть MultiRDP. " + error.Message);
                }
                finally
                {
                    instance.ReleaseMutex();
                }
            }
        }

        private static void RunMainWindow(MainWindow window, bool startHidden)
        {
            // WinForms automatically shows any form assigned to ApplicationContext.MainForm.
            using (var context = new ApplicationContext())
            {
                EventHandler exit = delegate { context.ExitThread(); };
                window.Disposed += exit;
                try
                {
                    if (!startHidden)
                        window.Show();
                    Application.Run(context);
                }
                finally { window.Disposed -= exit; }
            }
        }
    }

    internal sealed partial class MainWindow : Form
    {
        private readonly ProfileStore store;
        private readonly EventWaitHandle activate;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip trayMenu;
        private readonly ToolTip toolTips = new ToolTip();
        private readonly System.Windows.Forms.Timer timer;
        private readonly Dictionary<Guid, RdpClient> clients = new Dictionary<Guid, RdpClient>();
        private readonly DataGridView grid = new DataGridView();
        private readonly Label empty = new Label();
        private readonly Button edit = CompactButton("Изменить", 88);
        private readonly Button delete = CompactButton("\uE74D", 30);
        private readonly Button sessionAction = CompactButton("Подключить", 110);
        private Form activeDialog;
        private bool exiting;

        public MainWindow(ProfileStore store, EventWaitHandle activate, Icon icon, Icon trayIcon)
        {
            this.store = store;
            this.activate = activate;
            Text = "MultiRDP " + typeof(MainWindow).Assembly.GetName().Version.ToString(3);
            Icon = icon;
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(650, 228);
            MinimumSize = new Size(610, 256);
            BackColor = Ui.Background;
            ForeColor = Ui.Ink;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(9, 6, 9, 6),
                ColumnCount = 1, RowCount = 3
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            var toolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty
            };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            var add = CompactButton("Добавить", 88);
            add.Click += delegate { EditProfile(null); };
            edit.Click += delegate { EditProfile(Selected); };
            SetButtonIcon(delete, "Удалить");
            delete.Click += delegate { DeleteProfile(); };
            tools.Controls.AddRange(new Control[] { add, edit, delete });
            toolbar.Controls.Add(tools, 0, 0);
            var settings = CompactButton("\uE713", 30);
            SetButtonIcon(settings, "Настройки");
            settings.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            settings.Click += delegate { OpenSettings(); };
            toolbar.Controls.Add(settings, 1, 0);
            layout.Controls.Add(toolbar, 0, 0);

            grid.Dock = DockStyle.Fill;
            grid.AccessibleName = "Подключения RDP";
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = Color.FromArgb(229, 236, 245);
            grid.RowHeadersVisible = false;
            grid.ReadOnly = true;
            grid.MultiSelect = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.DefaultCellStyle.Padding = new Padding(7, 4, 6, 4);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(7, 3, 0, 3);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(220, 234, 252);
            grid.DefaultCellStyle.SelectionForeColor = Ui.Ink;
            AddColumn("Название", ProfileSort.Name);
            AddColumn("Сервер", ProfileSort.Address);
            AddColumn("Пользователь", ProfileSort.UserName);
            AddColumn("Состояние", ProfileSort.Status);
            var statusColumn = grid.Columns[3];
            statusColumn.DefaultCellStyle.Padding = new Padding(26, 4, 6, 4);
            ConfigurePinColumn();
            ConfigureColumnLayout();
            grid.CellFormatting += FormatStatusCell;
            grid.CellPainting += PaintStatusCell;
            ConfigureOrdering();
            grid.SelectionChanged += delegate { UpdateButtons(); };
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs args)
            {
                if (args.RowIndex >= 0 && args.ColumnIndex != pinColumn.Index)
                    ConnectSelected();
            };
            var body = new Panel
            {
                Dock = DockStyle.Fill, BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle, Margin = Padding.Empty
            };
            empty.Text = "Добавьте первое подключение";
            empty.TextAlign = ContentAlignment.MiddleCenter;
            empty.Dock = DockStyle.Fill;
            empty.ForeColor = Color.FromArgb(77, 95, 116);
            body.Controls.Add(grid);
            body.Controls.Add(empty);
            layout.Controls.Add(body, 0, 1);

            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Padding = new Padding(0, 3, 0, 0), Margin = Padding.Empty
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var links = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            links.Controls.Add(CreateLinkButton("GitHub", "https://github.com/ezh-HaMMeR/multirdp"));
            links.Controls.Add(CreateLinkButton("Telegram", "https://t.me/freaksty1e"));
            footer.Controls.Add(links, 0, 0);

            sessionAction.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            sessionAction.FlatStyle = FlatStyle.Flat;
            sessionAction.Click += delegate
            {
                if (clients.ContainsKey(SelectedId))
                    DisconnectSelected();
                else
                    ConnectSelected();
            };
            footer.Controls.Add(sessionAction, 1, 0);
            layout.Controls.Add(footer, 0, 2);
            Controls.Add(layout);

            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Открыть MultiRDP", null, delegate { RestoreWindow(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Выход", null, delegate { ExitFromTray(); });
            tray = new NotifyIcon { Text = "MultiRDP", Icon = trayIcon, ContextMenuStrip = trayMenu, Visible = true };
            tray.MouseDoubleClick += delegate(object sender, MouseEventArgs args)
            {
                if (args.Button == MouseButtons.Left)
                    RestoreWindow();
            };
            timer = new System.Windows.Forms.Timer { Interval = 350 };
            timer.Tick += delegate { Poll(); };
            timer.Start();
            RefreshRows(Guid.Empty);
        }

        private Profile Selected
        {
            get { return grid.SelectedRows.Count == 0 ? null : grid.SelectedRows[0].Tag as Profile; }
        }

        private Guid SelectedId
        {
            get { return Selected == null ? Guid.Empty : Selected.Id; }
        }

        private static Button CompactButton(string text, int width)
        {
            var button = Ui.Button(text, width);
            button.Height = 28;
            button.Margin = new Padding(0, 2, 5, 2);
            return button;
        }

        private void SetButtonIcon(Button button, string label)
        {
            button.Font = new Font("Segoe MDL2 Assets", 11);
            button.AccessibleName = label;
            toolTips.SetToolTip(button, label);
        }

        private void AddColumn(string caption, ProfileSort sort)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = caption, SortMode = DataGridViewColumnSortMode.Programmatic,
                Tag = sort, ToolTipText = "По возрастанию → по убыванию → ручной порядок"
            });
        }

        private void RefreshRows(Guid selected)
        {
            SetDropTarget(-1);
            grid.Rows.Clear();
            DataGridViewRow selectedRow = null;
            foreach (var profile in OrderedProfiles())
            {
                bool pinned = IsPinned(profile.Id);
                int index = grid.Rows.Add(profile.Name, profile.Address, profile.UserName, GetState(profile.Id), pinned);
                var row = grid.Rows[index];
                row.Tag = profile;
                row.Cells[pinColumn.Index].ToolTipText = pinned ? "Открепить" : "Закрепить наверху";
                if (profile.Id == selected)
                    selectedRow = row;
            }
            bool hasProfiles = grid.Rows.Count > 0;
            empty.Visible = !hasProfiles;
            grid.Visible = hasProfiles;
            if (selectedRow != null)
                grid.CurrentCell = selectedRow.Cells[0];
            UpdateSortHeaders();
            MeasureColumns();
            UpdateButtons();
        }

        private SessionState GetState(Guid id)
        {
            RdpClient client;
            return clients.TryGetValue(id, out client) ? client.State : SessionState.Disconnected;
        }

        private static string StatusText(SessionState state)
        {
            switch (state)
            {
                case SessionState.Connected: return "Подключено";
                case SessionState.Connecting: return "Подключение…";
                case SessionState.Reconnecting: return "Переподключение";
                default: return "Отключено";
            }
        }

        private static void FormatStatusCell(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.Value is SessionState)
            {
                e.Value = StatusText((SessionState)e.Value);
                e.FormattingApplied = true;
            }
        }

        private static void PaintStatusCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (!(e.Value is SessionState))
                return;
            var state = (SessionState)e.Value;
            var color = Color.FromArgb(150, 87, 0);
            if (state == SessionState.Connected)
                color = Color.FromArgb(23, 121, 67);
            else if (state == SessionState.Disconnected)
                color = Color.FromArgb(182, 49, 55);
            if (SystemInformation.HighContrast)
                color = SystemColors.WindowText;

            e.Paint(e.ClipBounds, e.PaintParts);
            int diameter = Math.Max(6, (int)Math.Round(e.CellStyle.Padding.Left * 0.39));
            int x = e.CellBounds.Left + (e.CellStyle.Padding.Left - diameter) / 2;
            int y = e.CellBounds.Top + (e.CellBounds.Height - diameter) / 2;
            var graphics = e.Graphics.Save();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(color))
                e.Graphics.FillEllipse(brush, x, y, diameter, diameter);
            e.Graphics.Restore(graphics);
            e.Handled = true;
        }

        private Button CreateLinkButton(string name, string url)
        {
            Bitmap icon;
            using (var stream = typeof(MainWindow).Assembly.GetManifestResourceStream(name + ".png"))
            using (var source = Image.FromStream(stream))
                icon = new Bitmap(source);
            var button = new Button
            {
                Size = new Size(29, 28), Margin = new Padding(0, 2, 4, 2),
                FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                AccessibleName = name, AccessibleRole = AccessibleRole.Link, AccessibleDescription = url
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(226, 234, 245);
            button.Paint += delegate(object sender, PaintEventArgs e)
            {
                int size = Math.Min(button.ClientSize.Width, button.ClientSize.Height) * 2 / 3;
                var graphics = e.Graphics.Save();
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(icon, (button.ClientSize.Width - size) / 2,
                    (button.ClientSize.Height - size) / 2, size, size);
                e.Graphics.Restore(graphics);
            };
            button.Click += delegate
            {
                try
                {
                    using (Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { }
                }
                catch (Exception error) { Ui.Error(this, "Не удалось открыть ссылку. " + error.Message); }
            };
            button.Disposed += delegate { icon.Dispose(); };
            toolTips.SetToolTip(button, name);
            return button;
        }

        private void UpdateButtons()
        {
            var selected = Selected;
            bool running = selected != null && clients.ContainsKey(selected.Id);
            edit.Enabled = selected != null;
            delete.Enabled = selected != null && !running;
            sessionAction.Enabled = selected != null;
            sessionAction.Text = running ? "Отключить" : "Подключить";
            bool primary = selected != null && !running && !SystemInformation.HighContrast;
            sessionAction.BackColor = primary ? Color.FromArgb(28, 93, 177) : SystemColors.Control;
            sessionAction.ForeColor = primary ? Color.White : SystemColors.ControlText;
            sessionAction.FlatAppearance.BorderSize = primary ? 0 : 1;
        }

        private DialogResult ShowOwned(Form dialog)
        {
            activeDialog = dialog;
            try { return dialog.ShowDialog(this); }
            finally { activeDialog = null; }
        }

        private void OpenSettings()
        {
            try
            {
                using (var dialog = new SettingsDialog(store, Icon))
                    ShowOwned(dialog);
            }
            catch (Exception error)
            {
                Ui.Error(this, "Не удалось открыть настройки. " + error.Message);
            }
        }

        private void EditProfile(Profile source)
        {
            using (var dialog = new ProfileDialog(source, Icon))
            {
                if (ShowOwned(dialog) != DialogResult.OK)
                    return;

                var previous = store.Profiles;
                store.Profiles = new List<Profile>(previous);
                if (source == null)
                    store.Profiles.Add(dialog.Result);
                else
                    store.Profiles[previous.IndexOf(source)] = dialog.Result;
                CommitProfiles(previous, dialog.Result.Id);
            }
        }

        private void DeleteProfile()
        {
            var selected = Selected;
            if (selected == null || clients.ContainsKey(selected.Id))
                return;
            if (MessageBox.Show(this, "Удалить подключение «" + selected.Name + "»?", "MultiRDP",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            var previous = store.Profiles;
            var previousPins = store.PinnedProfiles;
            store.Profiles = previous.Where(profile => profile.Id != selected.Id).ToList();
            store.PinnedProfiles = previousPins.Where(id => id != selected.Id).ToList();
            CommitProfiles(previous, Guid.Empty, previousPins);
        }

        private void CommitProfiles(List<Profile> previous, Guid select, List<Guid> previousPins = null)
        {
            try { store.Save(); }
            catch (Exception error)
            {
                store.Profiles = previous;
                if (previousPins != null)
                    store.PinnedProfiles = previousPins;
                Ui.Error(this, "Не удалось сохранить список подключений. " + error.Message);
                return;
            }
            RefreshRows(select);
        }

        private void ConnectSelected()
        {
            var selected = Selected;
            if (selected == null || clients.ContainsKey(selected.Id))
                return;
            try
            {
                clients.Add(selected.Id, RdpClient.Start(selected));
                RefreshStatus();
            }
            catch (Exception error)
            {
                Ui.Error(this, "Не удалось открыть подключение. " + error.Message);
            }
        }

        private void DisconnectSelected()
        {
            var selected = Selected;
            RdpClient client;
            if (selected == null || !clients.TryGetValue(selected.Id, out client))
                return;
            try
            {
                if (!client.Disconnect())
                    Ui.Error(this, "Закройте диалог клиента RDP или дождитесь открытия его окна и повторите отключение.");
            }
            catch (Exception error)
            {
                Ui.Error(this, "Не удалось закрыть клиент RDP. " + error.Message);
            }
        }

        private void Poll()
        {
            if (activate.WaitOne(0))
                RestoreWindow();
            ScrollDuringDrag();

            var finished = clients.Where(entry => entry.Value.HasExited).Select(entry => entry.Key).ToList();
            foreach (var id in finished)
            {
                clients[id].Dispose();
                clients.Remove(id);
            }
            bool changed = finished.Count > 0;
            foreach (var client in clients.Values)
                changed |= client.TakeStateChange();
            if (changed)
                RefreshStatus();
        }

        private void RefreshStatus()
        {
            if (store.SortColumn == ProfileSort.Status && draggedId == Guid.Empty)
            {
                RefreshRows(SelectedId);
                return;
            }
            foreach (DataGridViewRow row in grid.Rows)
                row.Cells[3].Value = GetState(((Profile)row.Tag).Id);
            UpdateButtons();
        }

        private void RestoreWindow()
        {
            if (activeDialog != null)
            {
                activeDialog.Activate();
                return;
            }
            Show();
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            Activate();
        }

        private void ExitFromTray()
        {
            if (activeDialog != null)
            {
                activeDialog.Activate();
                return;
            }
            if (clients.Count > 0)
            {
                RestoreWindow();
                if (MessageBox.Show(this, "Открытые окна RDP продолжат работать. Выйти из MultiRDP?",
                    "MultiRDP", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
            }
            exiting = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel)
                return;
            SaveWindowLayout(e.CloseReason == CloseReason.UserClosing);
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (timer != null)
                    timer.Dispose();
                if (tray != null)
                    tray.Dispose();
                if (trayMenu != null)
                    trayMenu.Dispose();
                toolTips.Dispose();
                foreach (var client in clients.Values)
                    client.Dispose();
                clients.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
