namespace WinMergeEdiPrediffer.Manager;

/// <summary>以左側列首拖曳排序；複製只提供目前儲存格的純文字。</summary>
internal sealed class RuleGridView : DataGridView
{
    private Rectangle dragBounds;
    private int dragRow = -1;
    private int insertionIndex = -1;
    private const string DragFormat = "WinMergeEdiPrediffer.Row";
    private readonly string dragToken = Guid.NewGuid().ToString("N");
    private object? draggedItem;

    internal event EventHandler<RowMoveEventArgs>? RowMoveRequested;

    internal RuleGridView()
    {
        Dock = DockStyle.Fill;
        AutoGenerateColumns = false;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToOrderColumns = false;
        AllowUserToResizeRows = false;
        MultiSelect = false;
        SelectionMode = DataGridViewSelectionMode.CellSelect;
        ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        RowHeadersWidth = 32;
        BackgroundColor = SystemColors.Window;
        EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        AllowDrop = true;
    }

    public override DataObject? GetClipboardContent()
    {
        if (CurrentCell is null) return null;
        var data = new DataObject();
        data.SetText(Convert.ToString(CurrentCell.Value) ?? "", TextDataFormat.UnicodeText);
        return data;
    }

    protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
    {
        base.OnCellPainting(e);
        if (e.ColumnIndex != -1 || e.Graphics is null) return;
        e.PaintBackground(e.ClipBounds, false);
        e.Paint(e.ClipBounds, DataGridViewPaintParts.Border);
        TextRenderer.DrawText(e.Graphics, e.RowIndex < 0 ? "↕" : "≡", Font, e.CellBounds,
            SystemColors.ControlText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        e.Handled = true;
    }

    protected override void OnCellToolTipTextNeeded(DataGridViewCellToolTipTextNeededEventArgs e)
    {
        base.OnCellToolTipTextNeeded(e);
        if (e.ColumnIndex == -1 && e.RowIndex >= 0) e.ToolTipText = "拖曳調整順序（由上往下執行）";
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        dragRow = -1;
        base.OnMouseDown(e);
        var hit = HitTest(e.X, e.Y);
        if (e.Button != MouseButtons.Left || hit.Type != DataGridViewHitTestType.RowHeader || !EndEdit()) return;
        CurrentCell = Rows[hit.RowIndex].Cells[Columns.Count > 1 ? 1 : 0];
        dragRow = hit.RowIndex;
        var size = SystemInformation.DragSize;
        dragBounds = new Rectangle(e.X - size.Width / 2, e.Y - size.Height / 2, size.Width, size.Height);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = HitTest(e.X, e.Y).Type == DataGridViewHitTestType.RowHeader ? Cursors.SizeAll : Cursors.Default;
        if (e.Button != MouseButtons.Left || dragRow < 0 || dragBounds.Contains(e.Location)) return;
        var source = dragRow;
        dragRow = -1;
        if (Rows[source].DataBoundItem is null) return;
        try { DoDragDrop(BeginRowDrag(source), DragDropEffects.Move); }
        finally { EndRowDrag(); }
    }

    internal DataObject BeginRowDrag(int rowIndex)
    {
        draggedItem = Rows[rowIndex].DataBoundItem;
        var data = new DataObject();
        // OLE 只攜帶識別字串，避免序列化控制項或規則物件。
        data.SetDataAsJson(DragFormat, dragToken);
        return data;
    }

    internal void EndRowDrag()
    {
        draggedItem = null; insertionIndex = -1; Invalidate();
    }

    private bool OwnDrag(IDataObject? data) => draggedItem is not null && data is ITypedDataObject typed
        && typed.TryGetData<string>(DragFormat, out var token) && token == dragToken;

    protected override void OnMouseUp(MouseEventArgs e)
    {
        dragRow = -1;
        base.OnMouseUp(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        var point = PointToClient(new Point(e.X, e.Y));
        var ownRow = OwnDrag(e.Data);
        if (ownRow && Rows.Count > 0 && ClientRectangle.Contains(point))
        {
            var first = FirstDisplayedScrollingRowIndex;
            if (first >= 0 && point.Y < ColumnHeadersHeight + RowTemplate.Height && first > 0)
                FirstDisplayedScrollingRowIndex = first - 1;
            else if (first >= 0 && point.Y > ClientSize.Height - RowTemplate.Height && first < Rows.Count - 1)
                FirstDisplayedScrollingRowIndex = first + 1;
        }
        var target = ownRow ? FindInsertionIndex(point) : -1;
        e.Effect = target >= 0 ? DragDropEffects.Move : DragDropEffects.None;
        if (insertionIndex != target) { insertionIndex = target; Invalidate(); }
    }

    protected override void OnDragLeave(EventArgs e)
    {
        insertionIndex = -1;
        Invalidate();
        base.OnDragLeave(e);
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        var target = FindInsertionIndex(PointToClient(new Point(e.X, e.Y)));
        insertionIndex = -1;
        Invalidate();
        if (target < 0 || !OwnDrag(e.Data)) return;
        var source = Rows.Cast<DataGridViewRow>().FirstOrDefault(r => ReferenceEquals(r.DataBoundItem, draggedItem));
        if (source is null) return;
        var destination = target > source.Index ? target - 1 : target;
        if (destination != source.Index) RowMoveRequested?.Invoke(this, new(source.Index, destination));
    }

    private int FindInsertionIndex(Point point)
    {
        if (Rows.Count == 0 || !ClientRectangle.Contains(point) || point.Y < ColumnHeadersHeight) return -1;
        var hit = HitTest(point.X, point.Y);
        if (hit.RowIndex >= 0)
        {
            var bounds = GetRowDisplayRectangle(hit.RowIndex, false);
            return hit.RowIndex + (point.Y >= bounds.Top + bounds.Height / 2 ? 1 : 0);
        }
        var last = GetRowDisplayRectangle(Rows.Count - 1, false);
        return last.Height > 0 && point.Y >= last.Bottom ? Rows.Count : -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (insertionIndex < 0 || Rows.Count == 0) return;
        var bounds = GetRowDisplayRectangle(Math.Min(insertionIndex, Rows.Count - 1), false);
        var y = insertionIndex == Rows.Count ? bounds.Bottom - 1 : bounds.Top;
        using var pen = new Pen(SystemColors.Highlight, 3);
        e.Graphics.DrawLine(pen, RowHeadersWidth, y, ClientSize.Width, y);
    }

}

internal sealed class RowMoveEventArgs(int sourceIndex, int destinationIndex) : EventArgs
{
    internal int SourceIndex { get; } = sourceIndex;
    internal int DestinationIndex { get; } = destinationIndex;
}
