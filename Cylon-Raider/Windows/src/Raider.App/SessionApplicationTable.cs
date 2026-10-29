using Raider.Core;

namespace Raider.App;

internal sealed class SessionApplicationTable : UserControl
{
    readonly DataGridView grid = AppTheme.Grid();
    Usage[] entries = [];
    int sortedColumn = -1;
    bool descending = true;
    internal DataGridView Grid => grid;

    public SessionApplicationTable()
    {
        Dock = DockStyle.Fill;
        AccessibleName = "All applications observed in the viewer session";
        grid.AccessibleName = AccessibleName;
        grid.VirtualMode = true;
        foreach (string title in new[] { "Application", "Observed download bytes", "Observed upload bytes", "Total observed bytes", "Executable / identity" })
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, SortMode = DataGridViewColumnSortMode.Programmatic, MinimumWidth = 110 });
        grid.Columns[0].FillWeight = 150;
        grid.Columns[4].FillWeight = 220;
        foreach (int column in new[] { 1, 2, 3 })
        {
            grid.Columns[column].ValueType = typeof(long);
            grid.Columns[column].DefaultCellStyle.Format = "N0";
            grid.Columns[column].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        grid.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= entries.Length) return;
            var row = entries[e.RowIndex];
            e.Value = e.ColumnIndex switch
            {
                0 => row.Name,
                1 => row.Incoming,
                2 => row.Outgoing,
                3 => checked(row.Incoming + row.Outgoing),
                _ => row.Path ?? (row.Key == "?" ? "Identity unavailable" : "Executable path unavailable; distinct process identity retained")
            };
        };
        grid.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.ColumnIndex < 0) return;
            descending = sortedColumn != e.ColumnIndex || !descending;
            sortedColumn = e.ColumnIndex;
            var sorted = entries.ToArray();
            Array.Sort(sorted, CompareRows);
            ReplaceRows(sorted);
            foreach (DataGridViewColumn column in grid.Columns)
                column.HeaderCell.SortGlyphDirection = column.Index == sortedColumn ? (descending ? SortOrder.Descending : SortOrder.Ascending) : SortOrder.None;
        };
        Controls.Add(grid);
    }

    int CompareRows(Usage left, Usage right)
    {
        int result = sortedColumn switch
        {
            0 => StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name),
            1 => left.Incoming.CompareTo(right.Incoming),
            2 => left.Outgoing.CompareTo(right.Outgoing),
            3 => checked(left.Incoming + left.Outgoing).CompareTo(checked(right.Incoming + right.Outgoing)),
            _ => StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path)
        };
        if (result == 0) return StringComparer.Ordinal.Compare(left.Key, right.Key);
        return descending ? -result : result;
    }

    // Update cell values in place. Sorting happens only when a column heading is
    // clicked; new identities append, so a refresh cannot move the reader's row.
    public void UpdateApplications(Usage[] applications)
    {
        var remaining = applications.ToDictionary(application => application.Key, StringComparer.Ordinal);
        var updated = new List<Usage>(applications.Length);
        foreach (var previous in entries)
            if (remaining.Remove(previous.Key, out var current)) updated.Add(current);
        foreach (var application in applications)
            if (remaining.Remove(application.Key)) updated.Add(application);
        ReplaceRows(updated.ToArray());
    }

    void ReplaceRows(Usage[] updated)
    {
        int first = grid.FirstDisplayedScrollingRowIndex;
        string? anchor = first >= 0 && first < entries.Length ? entries[first].Key : null;
        int selectedIndex = grid.CurrentCell?.RowIndex ?? -1;
        int selectedColumn = grid.CurrentCell?.ColumnIndex ?? 0;
        string? selected = selectedIndex >= 0 && selectedIndex < entries.Length ? entries[selectedIndex].Key : null;
        int horizontal = grid.HorizontalScrollingOffset;
        grid.SuspendLayout();
        try
        {
            entries = updated;
            if (grid.RowCount != entries.Length) grid.RowCount = entries.Length;
            int selectedRow = selected == null ? -1 : Array.FindIndex(entries, row => row.Key == selected);
            // Avoid assigning CurrentCell during ordinary value refreshes; that
            // assignment scrolls to the selected row even if the reader moved away.
            var currentCell = grid.CurrentCell;
            if (selectedRow >= 0 && (currentCell == null || currentCell.RowIndex != selectedRow || currentCell.ColumnIndex != selectedColumn))
                grid.CurrentCell = grid[selectedColumn, selectedRow];
            int anchorRow = anchor == null ? -1 : Array.FindIndex(entries, row => row.Key == anchor);
            if (anchorRow >= 0 && grid.FirstDisplayedScrollingRowIndex != anchorRow)
                grid.FirstDisplayedScrollingRowIndex = anchorRow;
            if (grid.HorizontalScrollingOffset != horizontal) grid.HorizontalScrollingOffset = horizontal;
            grid.Invalidate();
        }
        finally { grid.ResumeLayout(); }
    }
}
