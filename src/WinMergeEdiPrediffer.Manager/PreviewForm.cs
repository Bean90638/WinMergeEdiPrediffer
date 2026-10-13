using System.Text;
using WinMergeEdiPrediffer.Core;

namespace WinMergeEdiPrediffer.Manager;

internal sealed class PreviewForm : Form
{
    private readonly TextBox path = new() { Dock = DockStyle.Fill, AccessibleName = "測試檔案完整路徑" };
    private readonly PreviewTextBox input = Area("原始內容");
    private readonly PreviewTextBox output = Area("預差異化後內容", true);
    private readonly Label result = new() { Dock = DockStyle.Fill, Text = "輸入完整路徑與內容，再按「測試套用」。此視窗只處理副本。" };
    private readonly ComboBox encoding = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly ComboBox step = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, AccessibleName = "規則執行步驟" };
    private IReadOnlyList<RuleStep> steps = [];
    private readonly RuleConfiguration configuration;
    internal PreviewForm(RuleConfiguration snapshot)
    {
        configuration = snapshot; Text = "EDI 規則測試預覽";
        Width = 1120; Height = 720; MinimumSize = new Size(800, 520);
        StartPosition = FormStartPosition.CenterParent; Font = new Font("Microsoft JhengHei UI", 10);
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 4, ColumnCount = 1 };
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); table.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        table.Controls.Add(path, 0, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        encoding.Items.AddRange(["自動（UTF-8／系統 ANSI）", "UTF-8", "Big5（950）", "UTF-16 LE"]); encoding.SelectedIndex = 0;
        var load = new Button { Text = "載入檔案", AutoSize = true }; load.Click += (_, _) => Run(LoadFile);
        var apply = new Button { Text = "測試套用 (&T)", AutoSize = true }; apply.Click += (_, _) => Run(Apply);
        step.SelectedIndexChanged += (_, _) => { if (step.SelectedIndex >= 0 && step.SelectedIndex < steps.Count) output.Content = steps[step.SelectedIndex].Text; };
        buttons.Controls.AddRange([encoding, load, apply, step]); table.Controls.Add(buttons, 0, 1);
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 500 };
        split.Panel1.Controls.Add(input); split.Panel2.Controls.Add(output);
        table.Controls.Add(split, 0, 2); table.Controls.Add(result, 0, 3); Controls.Add(table);
        Load += (_, _) => split.SplitterDistance = (split.ClientSize.Width - split.SplitterWidth) / 2;
        input.Content = "ST*850*0001~\r\nSE*16*0001~\r\n"; path.Text = @"D:\ProjectGit\Brad\EDI\old\A_850.edi";
    }
    private static PreviewTextBox Area(string name, bool readOnly = false) => new()
    {
        Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, WordWrap = false,
        Font = new Font("Consolas", 11), ReadOnly = readOnly, AccessibleName = name, MaxLength = int.MaxValue,
        AcceptsReturn = true, AcceptsTab = true
    };
    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            steps = []; step.Items.Clear(); output.Content = "";
            result.Text = "無法套用：" + ex.Message;
        }
    }
    private void LoadFile()
    {
        using var dialog = new OpenFileDialog { Filter = "所有檔案|*.*", FileName = path.Text };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        LoadFile(dialog.FileName);
    }
    internal void LoadFile(string filename)
    {
        path.Text = Path.GetFullPath(filename); Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var selected = encoding.SelectedIndex switch { 2 => Encoding.GetEncoding(950), 3 => Encoding.Unicode, _ => new UTF8Encoding(false, true) };
        try { using var reader = new StreamReader(path.Text, selected, true); input.Content = reader.ReadToEnd(); }
        catch (DecoderFallbackException) when (encoding.SelectedIndex == 0)
        {
            input.Content = File.ReadAllText(path.Text, Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage));
        }
        Apply();
    }
    private void Apply()
    {
        if (!Path.IsPathFullyQualified(path.Text)) throw new InvalidOperationException("請填完整檔案路徑，讓路徑與檔名正則能正確選組。");
        var matches = RuleEngine.OrderedGroups(configuration).Where(g => RuleEngine.IsPathMatch(g, path.Text)).ToArray();
        var group = matches.FirstOrDefault();
        steps = group is null ? [] : RuleEngine.Preview(group, input.Content);
        step.Items.Clear();
        for (var i = 0; i < steps.Count; i++) step.Items.Add($"{i + 1}. {steps[i].Name}（{(steps[i].Changed ? "變更" : "無變更")}）");
        if (steps.Count > 0) step.SelectedIndex = steps.Count - 1;
        else output.Content = input.Content;
        result.Text = group is null ? "沒有命中啟用的規則組；內容保持原樣。"
            : $"符合組：{string.Join("、", matches.Select(g => g.Name))}；採用：{group.Name}。執行 {steps.Count} 條規則；可切換步驟查看結果。原始檔案不會被寫入。";
    }
}
