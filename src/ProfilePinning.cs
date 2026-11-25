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
            pinColumn.HeaderText = "";
            pinColumn.ToolTipText = "Закрепление подключений";
            pinColumn.Width = 32;
            pinColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
            pinColumn.Resizable = DataGridViewTriState.False;
            pinColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns.Add(pinColumn);
            pinColumn.DisplayIndex = 0;
            grid.Disposed += delegate { iconFont.Dispose(); };

            grid.CellFormatting += delegate(object sender, DataGridViewCellFormattingEventArgs e)
            {
                if (e.ColumnIndex != pinColumn.Index || !(e.Value is bool))
                    return;
                e.Value = (bool)e.Value ? "Закреплено" : "Не закреплено";
                e.FormattingApplied = true;
            };
            grid.CellPainting += delegate(object sender, DataGridViewCellPaintingEventArgs e)
            {
                if (e.ColumnIndex != pinColumn.Index || !(e.Value is bool))
                    return;
                e.Paint(e.ClipBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground);
                if ((e.PaintParts & DataGridViewPaintParts.ContentForeground) != 0)
                {
                    bool pinned = (bool)e.Value;
                    bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
                    Color color = selected ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor;
                    if (pinned && !SystemInformation.HighContrast)
                        color = Color.FromArgb(28, 93, 177);

                    // Draw the glyph alone so the text cell cannot add an ellipsis.
                    var graphicsState = e.Graphics.Save();
                    e.Graphics.IntersectClip(e.ClipBounds);
                    TextRenderer.DrawText(e.Graphics, pinned ? PinnedGlyph : PinGlyph, iconFont, e.CellBounds, color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
                    e.Graphics.Restore(graphicsState);
                }
                e.Handled = true;
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
