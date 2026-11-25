using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MultiRdp
{
    internal sealed partial class MainWindow
    {
        private Guid dragCandidate;
        private Guid draggedId;
        private Rectangle dragThreshold;
        private Point? dragPoint;
        private int dropTarget = -1;

        private void ConfigureOrdering()
        {
            grid.ColumnHeaderMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left)
                    return;
                var column = (ProfileSort)grid.Columns[e.ColumnIndex].Tag;
                bool sameColumn = store.SortColumn == column;
                if (sameColumn && store.SortDescending)
                    SetOrder(ProfileSort.Manual, false, store.Profiles, SelectedId);
                else
                    SetOrder(column, sameColumn, store.Profiles, SelectedId);
            };
            grid.AllowDrop = true;
            grid.MouseDown += RememberDragStart;
            grid.MouseMove += StartRowDrag;
            grid.DragOver += OnRowDragOver;
            grid.DragLeave += delegate { ClearDropTarget(); };
            grid.DragDrop += OnRowDrop;
            grid.RowPostPaint += delegate(object sender, DataGridViewRowPostPaintEventArgs e)
            {
                if (e.RowIndex != dropTarget)
                    return;
                var border = Rectangle.Intersect(e.RowBounds, grid.ClientRectangle);
                border.Inflate(-1, -1);
                if (border.Width > 0 && border.Height > 0)
                    e.Graphics.DrawRectangle(SystemPens.Highlight, border);
            };
        }

        private IEnumerable<Profile> OrderedProfiles()
        {
            Func<Profile, string> key;
            switch (store.SortColumn)
            {
                case ProfileSort.Name: key = profile => profile.Name; break;
                case ProfileSort.Address: key = profile => profile.Address; break;
                case ProfileSort.UserName: key = profile => profile.UserName; break;
                case ProfileSort.Status: key = profile => StatusText(GetState(profile.Id)); break;
                default: return store.Profiles;
            }
            return store.SortDescending
                ? store.Profiles.OrderByDescending(key, StringComparer.CurrentCultureIgnoreCase)
                : store.Profiles.OrderBy(key, StringComparer.CurrentCultureIgnoreCase);
        }

        private void UpdateSortHeaders()
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                var direction = store.SortDescending ? SortOrder.Descending : SortOrder.Ascending;
                column.HeaderCell.SortGlyphDirection =
                    (ProfileSort)column.Tag == store.SortColumn ? direction : SortOrder.None;
            }
        }

        private void SetOrder(ProfileSort column, bool descending, List<Profile> profiles, Guid select)
        {
            var previousColumn = store.SortColumn;
            var previousDirection = store.SortDescending;
            var previousProfiles = store.Profiles;
            store.SortColumn = column;
            store.SortDescending = descending;
            store.Profiles = profiles;
            try { store.Save(); }
            catch (Exception error)
            {
                store.SortColumn = previousColumn;
                store.SortDescending = previousDirection;
                store.Profiles = previousProfiles;
                Ui.Error(this, "Не удалось сохранить порядок подключений. " + error.Message);
                return;
            }
            RefreshRows(select);
        }

        private void RememberDragStart(object sender, MouseEventArgs e)
        {
            dragCandidate = Guid.Empty;
            if (e.Button != MouseButtons.Left || grid.Rows.Count < 2)
                return;
            var hit = grid.HitTest(e.X, e.Y);
            if (hit.Type != DataGridViewHitTestType.Cell)
                return;
            dragCandidate = ((Profile)grid.Rows[hit.RowIndex].Tag).Id;
            var size = SystemInformation.DragSize;
            dragThreshold = new Rectangle(e.X - size.Width / 2, e.Y - size.Height / 2, size.Width, size.Height);
        }

        private void StartRowDrag(object sender, MouseEventArgs e)
        {
            if (dragCandidate == Guid.Empty || e.Button != MouseButtons.Left || dragThreshold.Contains(e.Location))
                return;
            draggedId = dragCandidate;
            dragCandidate = Guid.Empty;
            try
            {
                grid.DoDragDrop(draggedId.ToString(), DragDropEffects.Move);
            }
            finally
            {
                draggedId = Guid.Empty;
                ClearDropTarget();
                if (store.SortColumn == ProfileSort.Status)
                    RefreshRows(SelectedId);
            }
        }

        private void OnRowDragOver(object sender, DragEventArgs e)
        {
            if (draggedId == Guid.Empty)
                return;
            dragPoint = grid.PointToClient(new Point(e.X, e.Y));
            int row = FindDropRow(dragPoint.Value);
            e.Effect = row >= 0 ? DragDropEffects.Move : DragDropEffects.None;
            SetDropTarget(row);
        }

        private int FindDropRow(Point point)
        {
            var hit = grid.HitTest(point.X, point.Y);
            if (hit.Type != DataGridViewHitTestType.Cell || ((Profile)grid.Rows[hit.RowIndex].Tag).Id == draggedId)
                return -1;
            return hit.RowIndex;
        }

        private void OnRowDrop(object sender, DragEventArgs e)
        {
            int target = FindDropRow(grid.PointToClient(new Point(e.X, e.Y)));
            ClearDropTarget();
            if (draggedId == Guid.Empty || target < 0)
                return;

            var profiles = grid.Rows.Cast<DataGridViewRow>().Select(row => (Profile)row.Tag).ToList();
            int source = profiles.FindIndex(profile => profile.Id == draggedId);
            if (source < 0)
                return;
            var moved = profiles[source];
            profiles[source] = profiles[target];
            profiles[target] = moved;
            SetOrder(ProfileSort.Manual, false, profiles, draggedId);
        }

        private void ScrollDuringDrag()
        {
            if (!dragPoint.HasValue || grid.FirstDisplayedScrollingRowIndex < 0)
                return;
            int top = grid.ColumnHeadersHeight;
            int bottom = grid.DisplayRectangle.Bottom;
            int y = dragPoint.Value.Y;
            int first = grid.FirstDisplayedScrollingRowIndex;
            int margin = grid.Rows[first].Height / 2;
            if (y >= top && y < top + margin && first > 0)
                grid.FirstDisplayedScrollingRowIndex = first - 1;
            else if (y < bottom && y >= bottom - margin && !grid.Rows[grid.Rows.Count - 1].Displayed)
                grid.FirstDisplayedScrollingRowIndex = first + 1;
            else
                return;
            SetDropTarget(FindDropRow(dragPoint.Value));
        }

        private void ClearDropTarget()
        {
            dragPoint = null;
            SetDropTarget(-1);
        }

        private void SetDropTarget(int row)
        {
            if (row == dropTarget)
                return;
            int previous = dropTarget;
            dropTarget = row;
            if (previous >= 0 && previous < grid.Rows.Count)
                grid.InvalidateRow(previous);
            if (row >= 0)
                grid.InvalidateRow(row);
        }
    }
}
