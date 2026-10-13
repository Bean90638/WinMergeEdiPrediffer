namespace WinMergeEdiPrediffer.Manager;

/// <summary>複製只提供目前儲存格的純文字。</summary>
internal sealed class RuleGridView : DataGridView
{
    public override DataObject? GetClipboardContent()
    {
        if (CurrentCell is null) return null;
        var data = new DataObject();
        data.SetText(Convert.ToString(CurrentCell.Value) ?? "", TextDataFormat.UnicodeText);
        return data;
    }
}
