using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MultiRdp
{
    internal sealed partial class MainWindow
    {
        private const string PinGlyph = "\uE718";
        private const string PinnedGlyph = "\uE842";
        // The pin is displayed first. Its collection index comes after the four saved column widths.
        private readonly DataGridViewTextBoxColumn pinColumn = new DataGridViewTextBoxColumn();

        private void ConfigurePinColumn()
        {
            var iconFont = new Font("Segoe MDL2 Assets", 10);
            pinColumn.HeaderText = PinGlyph;
            pinColumn.ToolTipText = "Закрепление подключений";
            pinColumn.Width = 32;
            pinColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
            pinColumn.Resizable = DataGridViewTriState.False;
            pinColumn.HeaderCell.Style.Font = iconFont;
            pinColumn.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            pinColumn.HeaderCell.Style.Padding = Padding.Empty;
            pinColumn.DefaultCellStyle.Font = iconFont;
            pinColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            pinColumn.DefaultCellStyle.Padding = Padding.Empty;
            grid.Columns.Add(pinColumn);
            pinColumn.DisplayIndex = 0;
            grid.Disposed += delegate { iconFont.Dispose(); };

            grid.CellFormatting += delegate(object sender, DataGridViewCellFormattingEventArgs e)
            {
                if (e.ColumnIndex != pinColumn.Index || !(e.Value is bool))
                    return;
                bool pinned = (bool)e.Value;
                e.Value = pinned ? PinnedGlyph : PinGlyph;
                if (pinned && !SystemInformation.HighContrast)
                {
                    var accent = Color.FromArgb(28, 93, 177);
                    e.CellStyle.ForeColor = accent;
                    e.CellStyle.SelectionForeColor = accent;
                }
                e.FormattingApplied = true;
            };
            grid.CellMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == pinColumn.Index && e.Button == MouseButtons.Left && e.Clicks == 1)
                    TogglePin(e.RowIndex);
            };
            grid.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                var cell = grid.CurrentCell;
                if (cell == null || cell.ColumnIndex != pinColumn.Index || e.Modifiers != Keys.None ||
                    (e.KeyCode != Keys.Space && e.KeyCode != Keys.Enter))
                    return;
                e.SuppressKeyPress = true;
                TogglePin(cell.RowIndex);
            };
        }

        private bool IsPinned(Guid id)
        {
            return store.PinnedProfiles.Contains(id);
        }

        private void TogglePin(int rowIndex)
        {
            var id = ((Profile)grid.Rows[rowIndex].Tag).Id;
            var pins = new List<Guid>(store.PinnedProfiles);
            if (!pins.Remove(id))
                pins.Add(id);
            SetOrder(store.SortColumn, store.SortDescending, store.Profiles, id, pins);
        }
    }
}
