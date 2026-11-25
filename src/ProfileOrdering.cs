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
                if (e.Button != MouseButtons.Left || e.ColumnIndex == pinColumn.Index)
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
            if (store.PinnedProfiles.Count == 0)
                return SortUnpinnedProfiles(store.Profiles);
            var profiles = store.Profiles.ToLookup(profile => profile.Id);
            var pins = new HashSet<Guid>(store.PinnedProfiles);
            var pinned = store.PinnedProfiles.SelectMany(id => profiles[id]);
            var others = store.Profiles.Where(profile => !pins.Contains(profile.Id));
            return pinned.Concat(SortUnpinnedProfiles(others));
        }

        private IEnumerable<Profile> SortUnpinnedProfiles(IEnumerable<Profile> profiles)
        {
            Func<Profile, string> key;
            switch (store.SortColumn)
            {
                case ProfileSort.Name: key = profile => profile.Name; break;
                case ProfileSort.Address: key = profile => profile.Address; break;
                case ProfileSort.UserName: key = profile => profile.UserName; break;
                case ProfileSort.Status: key = profile => StatusText(GetState(profile.Id)); break;
                default: return profiles;
            }
            return store.SortDescending
                ? profiles.OrderByDescending(key, StringComparer.CurrentCultureIgnoreCase)
                : profiles.OrderBy(key, StringComparer.CurrentCultureIgnoreCase);
        }

        private void UpdateSortHeaders()
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column.SortMode == DataGridViewColumnSortMode.NotSortable)
                    continue;
                var direction = store.SortDescending ? SortOrder.Descending : SortOrder.Ascending;
                column.HeaderCell.SortGlyphDirection =
                    (ProfileSort)column.Tag == store.SortColumn ? direction : SortOrder.None;
            }
        }

        private void SetOrder(ProfileSort column, bool descending, List<Profile> profiles, Guid select,
            List<Guid> pinnedProfiles = null)
        {
            var previousColumn = store.SortColumn;
            var previousDirection = store.SortDescending;
            var previousProfiles = store.Profiles;
            var previousPins = store.PinnedProfiles;
            store.SortColumn = column;
            store.SortDescending = descending;
            store.Profiles = profiles;
            store.PinnedProfiles = pinnedProfiles ?? previousPins;
            try { store.Save(); }
            catch (Exception error)
            {
                store.SortColumn = previousColumn;
                store.SortDescending = previousDirection;
                store.Profiles = previousProfiles;
                store.PinnedProfiles = previousPins;
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
            if (hit.Type != DataGridViewHitTestType.Cell || hit.ColumnIndex == pinColumn.Index)
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
            if (hit.Type != DataGridViewHitTestType.Cell)
                return -1;
            Guid targetId = ((Profile)grid.Rows[hit.RowIndex].Tag).Id;
            return targetId != draggedId && IsPinned(targetId) == IsPinned(draggedId) ? hit.RowIndex : -1;
        }

        private void OnRowDrop(object sender, DragEventArgs e)
        {
            int target = FindDropRow(grid.PointToClient(new Point(e.X, e.Y)));
            ClearDropTarget();
            if (draggedId == Guid.Empty || target < 0)
                return;

            var targetId = ((Profile)grid.Rows[target].Tag).Id;
            if (IsPinned(draggedId))
            {
                var pinned = new List<Guid>(store.PinnedProfiles);
                Swap(pinned, pinned.IndexOf(draggedId), pinned.IndexOf(targetId));
                SetOrder(store.SortColumn, store.SortDescending, store.Profiles, draggedId, pinned);
                return;
            }

            var pins = new HashSet<Guid>(store.PinnedProfiles);
            var profiles = grid.Rows.Cast<DataGridViewRow>().Select(row => (Profile)row.Tag)
                .Where(profile => !pins.Contains(profile.Id)).ToList();
            int source = profiles.FindIndex(profile => profile.Id == draggedId);
            if (source < 0)
                return;
            Swap(profiles, source, profiles.FindIndex(profile => profile.Id == targetId));
            var reordered = new Queue<Profile>(profiles);
            var manualOrder = store.Profiles.Select(profile => pins.Contains(profile.Id) ? profile : reordered.Dequeue()).ToList();
            SetOrder(ProfileSort.Manual, false, manualOrder, draggedId);
        }

        private static void Swap<T>(List<T> items, int source, int target)
        {
            var item = items[source];
            items[source] = items[target];
            items[target] = item;
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
