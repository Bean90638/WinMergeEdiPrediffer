using System.Runtime.InteropServices;

namespace WinMergeEdiPrediffer.Manager;

/// <summary>Windows EDIT 使用 CRLF 顯示，預覽及複製仍使用原始 CRLF、LF 或 CR。</summary>
internal sealed class PreviewTextBox : TextBox
{
    private string content = "";
    private string displayedText = "";
    private bool updating;
    private bool undoing;
    private string? undoContent;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string Content
    {
        get => content;
        set
        {
            updating = true;
            try { content = value; Text = ForDisplay(value); displayedText = Text; undoContent = null; ClearUndo(); }
            finally { updating = false; }
        }
    }

    private static string ForDisplay(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n').Replace("\n", "\r\n", StringComparison.Ordinal);

    protected override void OnTextChanged(EventArgs e)
    {
        if (!updating && !undoing)
        {
            var previous = content;
            var current = Text;
            var prefix = 0;
            while (prefix < displayedText.Length && prefix < current.Length && displayedText[prefix] == current[prefix]) prefix++;
            // 差異範圍不可從 CRLF 的中間切開。
            if (prefix > 0 && prefix < displayedText.Length && displayedText[prefix - 1] == '\r' && displayedText[prefix] == '\n') prefix--;
            var suffix = 0;
            while (suffix < displayedText.Length - prefix && suffix < current.Length - prefix
                && displayedText[^(suffix + 1)] == current[^(suffix + 1)]) suffix++;
            var end = displayedText.Length - suffix;
            if (end > prefix && end < displayedText.Length && displayedText[end - 1] == '\r' && displayedText[end] == '\n') suffix--;
            var startOffset = ContentOffset(prefix);
            var endOffset = ContentOffset(displayedText.Length - suffix);
            content = content[..startOffset] + current[prefix..(current.Length - suffix)] + content[endOffset..];
            displayedText = current;
            undoContent = previous;
        }
        base.OnTextChanged(e);
    }

    private int ContentOffset(int displayOffset)
    {
        var source = 0;
        var display = 0;
        while (source < content.Length && display < displayOffset)
        {
            if (content[source] == '\r' && source + 1 < content.Length && content[source + 1] == '\n')
            { source += 2; display += 2; }
            else { display += content[source] is '\r' or '\n' ? 2 : 1; source++; }
        }
        return source;
    }

    internal void PasteContent(string text)
    {
        var start = ContentOffset(SelectionStart);
        var end = ContentOffset(SelectionStart + SelectionLength);
        updating = true;
        try
        {
            undoContent = content;
            content = content[..start] + text + content[end..];
            // 保留原生文字框的復原／重做；SelectedText 的程式設定會清除復原紀錄。
            SendMessage(Handle, 0x00C2, new IntPtr(1), ForDisplay(text));
            displayedText = Text;
        }
        finally { updating = false; }
    }

    internal string SelectedContent => content[ContentOffset(SelectionStart)..ContentOffset(SelectionStart + SelectionLength)];

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr parameter, string text);

    protected override void WndProc(ref Message m)
    {
        const int copy = 0x0301, cut = 0x0300, paste = 0x0302;
        const int undo = 0x0304, editUndo = 0x00C7;
        if (m.Msg is undo or editUndo)
        {
            var previous = content;
            var restore = undoContent;
            undoing = true;
            try { base.WndProc(ref m); }
            finally { undoing = false; }
            if (restore is not null && ForDisplay(restore) == Text)
            { content = restore; displayedText = Text; undoContent = previous; }
            else if (Text != displayedText) OnTextChanged(EventArgs.Empty);
            return;
        }
        if (m.Msg == paste && !ReadOnly && Clipboard.ContainsText())
        { PasteContent(Clipboard.GetText()); return; }
        if (m.Msg is copy or cut && SelectionLength > 0)
        {
            Clipboard.SetText(SelectedContent);
            if (m.Msg == cut && !ReadOnly) PasteContent("");
            return;
        }
        base.WndProc(ref m);
    }
}
