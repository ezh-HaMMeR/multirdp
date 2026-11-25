using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MultiRdp
{
    internal sealed partial class MainWindow
    {
        private const int MaximumColumnWidth = 65536;
        private int[] manualWidths;
        private int[] contentWidths;
        private float layoutScale = 1;
        private bool layoutReady;
        private bool arrangingColumns;

        private void ConfigureColumnLayout()
        {
            manualWidths = new int[grid.Columns.Count - 1];
            contentWidths = new int[grid.Columns.Count];
            var saved = store.WindowLayout;
            if (saved != null && saved.ColumnWidths != null)
                Array.Copy(saved.ColumnWidths, manualWidths, Math.Min(saved.ColumnWidths.Length, manualWidths.Length));

            grid.ColumnWidthChanged += delegate(object sender, DataGridViewColumnEventArgs e)
            {
                if (layoutReady && !arrangingColumns && e.Column != pinColumn)
                    manualWidths[e.Column.Index] = ToLogicalPixels(e.Column.Width);
            };
            grid.SizeChanged += delegate { ArrangeColumns(); };
            grid.MouseUp += delegate { ArrangeColumns(); };
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            using (var graphics = CreateGraphics())
                layoutScale = graphics.DpiX / 96f;

            // Measure after WinForms has scaled the controls for this display.
            MeasureColumnContent();
            var workingArea = Screen.FromControl(this).WorkingArea;
            var saved = store.WindowLayout;
            var size = Size;
            if (saved != null && saved.Width > 0 && saved.Height > 0)
                size = new Size(ToDevicePixels(saved.Width), ToDevicePixels(saved.Height));
            else if (grid.Rows.Count > 0)
            {
                int requiredWidth = RequiredColumnWidth();
                size.Width += Math.Max(0, requiredWidth - AvailableColumnWidth(requiredWidth));
            }

            Size = new Size(
                Math.Max(MinimumSize.Width, Math.Min(size.Width, workingArea.Width)),
                Math.Max(MinimumSize.Height, Math.Min(size.Height, workingArea.Height)));
            Location = new Point(
                workingArea.Left + Math.Max(0, (workingArea.Width - Width) / 2),
                workingArea.Top + Math.Max(0, (workingArea.Height - Height) / 2));
            layoutReady = true;
            ArrangeColumns();
        }

        private void MeasureColumns()
        {
            if (!layoutReady)
                return;
            MeasureColumnContent();
            ArrangeColumns();
        }

        private void MeasureColumnContent()
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column == pinColumn)
                {
                    contentWidths[column.Index] = ToDevicePixels(32);
                    continue;
                }
                int width = column.GetPreferredWidth(DataGridViewAutoSizeColumnMode.AllCells, true);
                if ((ProfileSort)column.Tag == ProfileSort.Status)
                {
                    var padding = column.DefaultCellStyle.Padding.Horizontal;
                    int statusWidth = Enum.GetValues(typeof(SessionState)).Cast<SessionState>()
                        .Max(state => TextRenderer.MeasureText(StatusText(state), grid.Font).Width);
                    width = Math.Max(width, statusWidth + padding + 2);
                }
                contentWidths[column.Index] = Math.Max(column.MinimumWidth, Math.Min(width, MaximumColumnWidth));
            }
        }

        private int ColumnWidth(int index)
        {
            if (index == pinColumn.Index || manualWidths[index] <= 0)
                return contentWidths[index];
            return Math.Max(grid.Columns[index].MinimumWidth, Math.Min(MaximumColumnWidth, ToDevicePixels(manualWidths[index])));
        }

        private int RequiredColumnWidth()
        {
            return Enumerable.Range(0, grid.Columns.Count).Sum(index => ColumnWidth(index));
        }

        private int AvailableColumnWidth(int requiredWidth)
        {
            int width = grid.ClientSize.Width;
            int height = grid.ClientSize.Height;
            if (requiredWidth > width)
                height -= SystemInformation.HorizontalScrollBarHeight;
            if (grid.ColumnHeadersHeight + grid.Rows.GetRowsHeight(DataGridViewElementStates.Visible) > height)
                width -= SystemInformation.VerticalScrollBarWidth;
            return Math.Max(0, width);
        }

        private void ArrangeColumns()
        {
            if (!layoutReady || arrangingColumns)
                return;

            int requiredWidth = RequiredColumnWidth();
            int spare = Math.Max(0, AvailableColumnWidth(requiredWidth) - requiredWidth);
            int automatic = manualWidths.Count(width => width <= 0);
            arrangingColumns = true;
            try
            {
                foreach (DataGridViewColumn column in grid.Columns)
                {
                    int width = ColumnWidth(column.Index);
                    if (column != pinColumn && manualWidths[column.Index] <= 0)
                    {
                        int extra = spare / automatic--;
                        width += extra;
                        spare -= extra;
                    }
                    width = Math.Min(width, MaximumColumnWidth);
                    if (column.Width != width)
                        column.Width = width;
                }
            }
            finally { arrangingColumns = false; }
        }

        private void SaveWindowLayout(bool reportFailure)
        {
            if (!layoutReady)
                return;
            var size = WindowState == FormWindowState.Normal ? Size : RestoreBounds.Size;
            var width = ToLogicalPixels(size.Width);
            var height = ToLogicalPixels(size.Height);
            var previous = store.WindowLayout;
            if (previous != null && previous.Width == width && previous.Height == height &&
                previous.ColumnWidths != null && previous.ColumnWidths.SequenceEqual(manualWidths))
                return;

            store.WindowLayout = new SavedWindowLayout
            {
                Width = width, Height = height, ColumnWidths = (int[])manualWidths.Clone()
            };
            try { store.Save(); }
            catch (Exception error)
            {
                store.WindowLayout = previous;
                if (reportFailure)
                    Ui.Error(this, "Не удалось сохранить размеры окна. " + error.Message);
            }
        }

        private int ToDevicePixels(int pixels)
        {
            return (int)Math.Min(int.MaxValue, Math.Round(pixels * (double)layoutScale));
        }

        private int ToLogicalPixels(int pixels)
        {
            return (int)Math.Round(pixels / (double)layoutScale);
        }
    }
}
