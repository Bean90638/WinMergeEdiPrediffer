using System.ComponentModel;
using WinMergeEdiPrediffer.Core;

namespace WinMergeEdiPrediffer.Manager;

internal sealed class MainForm : Form
{
    private RuleConfiguration configuration;
    private string configurationPath;
    private bool loading;
    private bool dirty;
    private ReplacementRule? editingRule;
    private readonly DataGridView groups = Grid("EDI 規則組");
    private readonly DataGridView rules = Grid("組內替換規則");
    private readonly TextBox find = Editor("尋找正則");
    private readonly TextBox replace = Editor("取代內容");
    private readonly ToolStripStatusLabel status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private RuleGroup? SelectedGroup => groups.CurrentRow?.DataBoundItem as RuleGroup;
    private ReplacementRule? SelectedRule => rules.CurrentRow?.DataBoundItem as ReplacementRule;

    internal MainForm(string? path)
    {
        configurationPath = Path.GetFullPath(path ?? AppPaths.DefaultConfiguration);
        configuration = ConfigurationJson.Deserialize(File.ReadAllText(File.Exists(configurationPath) ? configurationPath : AppPaths.Example));
        ConfigurationValidator.Validate(configuration);
        Text = "EDI 預差異化規則管理器 — C# COM";
        Width = 1240; Height = 850; MinimumSize = new Size(1000, 700);
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Microsoft JhengHei UI", 10);
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        Tool(toolbar, "新增設定", NewConfiguration); Tool(toolbar, "開啟設定", OpenConfiguration);
        Tool(toolbar, "儲存 (Ctrl+S)", () => Save()); Tool(toolbar, "另存新檔", () => Save(true));
        toolbar.Items.Add(new ToolStripSeparator());
        Tool(toolbar, "測試預覽 (Ctrl+T)", Preview); Tool(toolbar, "匯出 COM 外掛", Export); Tool(toolbar, "套用到 WinMerge", Install);
        Column(groups, nameof(RuleGroup.Enabled), "啟用", 60, true);
        Column(groups, nameof(RuleGroup.Name), "組名稱", 170);
        Column(groups, nameof(RuleGroup.Priority), "優先序（小數字先）", 155);
        Column(groups, nameof(RuleGroup.IsDefault), "預設組", 75, true);
        Column(groups, nameof(RuleGroup.FilePattern), "完整路徑＋檔名正則（預設 .*）", 500, fill: true);
        Column(rules, nameof(ReplacementRule.Enabled), "啟用", 60, true);
        Column(rules, nameof(ReplacementRule.Name), "規則名稱（由上往下執行）", 260);
        Column(rules, nameof(ReplacementRule.UseRegularExpression), "正則", 60, true);
        Column(rules, nameof(ReplacementRule.IgnoreCase), "忽略大小寫", 105, true);
        Column(rules, nameof(ReplacementRule.FindPattern), "尋找", 300, fill: true);
        Column(rules, nameof(ReplacementRule.Replacement), "取代", 200, fill: true);
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 7 };
        foreach (var style in new[]
        {
            new RowStyle(SizeType.Absolute, 26), new RowStyle(SizeType.Percent, 44), new RowStyle(SizeType.Absolute, 42),
            new RowStyle(SizeType.Percent, 56), new RowStyle(SizeType.Absolute, 42), new RowStyle(SizeType.Absolute, 145), new RowStyle(SizeType.Absolute, 52)
        }) table.RowStyles.Add(style);
        table.Controls.Add(new Label { Text = "多組 EDI：優先序小的專屬組先處理；預設組最後；每個檔案只套用第一個命中的組。", Dock = DockStyle.Fill }, 0, 0);
        table.Controls.Add(groups, 0, 1);
        var groupButtons = Buttons();
        groupButtons.Controls.AddRange([Button("新增組 (&G)", AddGroup), Button("複製組", DuplicateGroup), Button("刪除組", DeleteGroup)]);
        table.Controls.Add(groupButtons, 0, 2); table.Controls.Add(rules, 0, 3);
        var ruleButtons = Buttons();
        ruleButtons.Controls.AddRange([Button("新增規則 (&R)", AddRule), Button("複製規則", DuplicateRule), Button("刪除規則", DeleteRule), Button("上移", () => MoveRule(-1)), Button("下移", () => MoveRule(1))]);
        table.Controls.Add(ruleButtons, 0, 4);
        var editors = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 2 };
        editors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); editors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        editors.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); editors.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editors.Controls.Add(new Label { Text = "所選規則：尋找", Dock = DockStyle.Fill }, 0, 0);
        editors.Controls.Add(new Label { Text = "所選規則：取代（正則可用 $1、${name}）", Dock = DockStyle.Fill }, 1, 0);
        editors.Controls.Add(find, 0, 1); editors.Controls.Add(replace, 1, 1); table.Controls.Add(editors, 0, 5);
        table.Controls.Add(new Label { Text = "新增組的路徑預設 .*。Windows 路徑請用 \\\\；路徑與內容正則均使用 C#/.NET 語法，內容保留 CR/LF。\n修改後按「套用到 WinMerge」；規則熱更新並刷新目前比對，WinMerge 與擴充功能視窗都可保持開啟。", Dock = DockStyle.Fill }, 0, 6);
        var statusBar = new StatusStrip(); statusBar.Items.Add(status);
        Controls.Add(table); Controls.Add(statusBar); Controls.Add(toolbar);
        groups.SelectionChanged += (_, _) => { if (!loading) BindRules(); };
        rules.SelectionChanged += (_, _) => ShowRule();
        foreach (var grid in new[] { groups, rules })
        {
            grid.CellValueChanged += (_, _) => { if (!loading) { Touch(); if (grid == rules) ShowRule(); } };
            grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.DataError += (_, e) => { e.ThrowException = false; status.Text = "欄位格式有誤；優先序請填整數。"; };
        }
        find.TextChanged += (_, _) => EditRule(); replace.TextChanged += (_, _) => EditRule();
        FormClosing += (_, e) => { if (!ConfirmPending()) e.Cancel = true; };
        BindGroups(); UpdateStatus();
    }
    private static DataGridView Grid(string name) => new RuleGridView()
    {
        Dock = DockStyle.Fill, AccessibleName = name, AutoGenerateColumns = false, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AllowUserToOrderColumns = false, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.CellSelect, RowHeadersWidth = 28,
        BackgroundColor = SystemColors.Window, EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
    };
    private static TextBox Editor(string name) => new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 11), AccessibleName = name };
    private static FlowLayoutPanel Buttons() => new() { Dock = DockStyle.Fill, WrapContents = false };
    private static void Column(DataGridView grid, string property, string title, int width, bool check = false, bool fill = false)
    {
        DataGridViewColumn column = check ? new DataGridViewCheckBoxColumn() : new DataGridViewTextBoxColumn();
        column.DataPropertyName = property; column.HeaderText = title; column.Name = property;
        column.Width = width; column.SortMode = DataGridViewColumnSortMode.NotSortable;
        if (fill) { column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; column.FillWeight = width; }
        grid.Columns.Add(column);
    }
    private Button Button(string title, Action action)
    {
        var button = new Button { Text = title, AutoSize = true, Height = 33 }; button.Click += (_, _) => Run(action); return button;
    }
    private void Tool(ToolStrip strip, string title, Action action)
    {
        var button = new ToolStripButton(title); button.Click += (_, _) => Run(action); strip.Items.Add(button);
    }
    private void Run(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "請檢查設定", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
    private void BindGroups(int selected = 0)
    {
        loading = true; groups.DataSource = new BindingList<RuleGroup>(configuration.Groups);
        if (groups.Rows.Count > 0) groups.CurrentCell = groups.Rows[Math.Clamp(selected, 0, groups.Rows.Count - 1)].Cells[1];
        loading = false; BindRules();
    }
    private void BindRules(int selected = 0)
    {
        loading = true; rules.DataSource = new BindingList<ReplacementRule>(SelectedGroup?.Rules ?? []);
        if (rules.Rows.Count > 0) rules.CurrentCell = rules.Rows[Math.Clamp(selected, 0, rules.Rows.Count - 1)].Cells[1];
        loading = false; ShowRule();
    }
    private void ShowRule()
    {
        if (loading) return; loading = true; editingRule = SelectedRule;
        find.Text = editingRule?.FindPattern ?? ""; replace.Text = editingRule?.Replacement ?? "";
        find.Enabled = replace.Enabled = editingRule is not null; loading = false;
    }
    private void EditRule()
    {
        if (loading || editingRule is null) return;
        editingRule.FindPattern = find.Text; editingRule.Replacement = replace.Text;
        rules.InvalidateRow(rules.CurrentRow!.Index); Touch();
    }
    private void Touch() { dirty = true; UpdateStatus(); }
    private void AddGroup() { configuration.Groups.Add(new RuleGroup()); BindGroups(configuration.Groups.Count - 1); Touch(); }
    private void DuplicateGroup()
    {
        if (SelectedGroup is not { } group) return;
        var copy = ConfigurationJson.Deserialize(ConfigurationJson.Serialize(new RuleConfiguration { Groups = [group] })).Groups[0];
        copy.Id = Guid.NewGuid().ToString("N"); copy.Name += "（複本）"; copy.IsDefault = false;
        configuration.Groups.Add(copy); BindGroups(configuration.Groups.Count - 1); Touch();
    }
    private void DeleteGroup()
    {
        if (SelectedGroup is not { } group || MessageBox.Show(this, $"刪除「{group.Name}」及其規則？", "刪除規則組", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        configuration.Groups.Remove(group); BindGroups(); Touch();
    }
    private void AddRule()
    {
        if (SelectedGroup is not { } group) return; group.Rules.Add(new ReplacementRule()); BindRules(group.Rules.Count - 1); Touch();
    }
    private void DuplicateRule()
    {
        if (SelectedGroup is not { } group || SelectedRule is not { } rule) return;
        group.Rules.Add(new ReplacementRule { Name = rule.Name + "（複本）", Enabled = rule.Enabled, UseRegularExpression = rule.UseRegularExpression, IgnoreCase = rule.IgnoreCase, FindPattern = rule.FindPattern, Replacement = rule.Replacement });
        BindRules(group.Rules.Count - 1); Touch();
    }
    private void DeleteRule()
    {
        if (SelectedGroup is not { } group || SelectedRule is not { } rule || MessageBox.Show(this, $"刪除「{rule.Name}」？", "刪除規則", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        group.Rules.Remove(rule); BindRules(); Touch();
    }
    private void MoveRule(int direction)
    {
        if (SelectedGroup is not { } group || SelectedRule is not { } rule) return;
        var index = group.Rules.IndexOf(rule); var target = index + direction;
        if (target < 0 || target >= group.Rules.Count) return;
        group.Rules.RemoveAt(index); group.Rules.Insert(target, rule); BindRules(target); Touch();
    }
    private bool Save(bool saveAs = false)
    {
        if (!groups.EndEdit() || !rules.EndEdit()) throw new InvalidOperationException("請先完成目前欄位的編輯。");
        ConfigurationValidator.Validate(configuration);
        if (saveAs)
        {
            using var dialog = new SaveFileDialog { Filter = "JSON 規則設定|*.json", FileName = Path.GetFileName(configurationPath), InitialDirectory = Path.GetDirectoryName(configurationPath) };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false; configurationPath = dialog.FileName;
        }
        AppPaths.SaveConfiguration(configuration, configurationPath); dirty = false; UpdateStatus(); return true;
    }
    private bool ConfirmPending()
    {
        if (!dirty) return true;
        var result = MessageBox.Show(this, "規則尚未儲存，是否先儲存？", "尚未儲存的設定", MessageBoxButtons.YesNoCancel);
        if (result == DialogResult.Cancel) return false; if (result == DialogResult.No) return true;
        try { return Save(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "無法儲存"); return false; }
    }
    private void NewConfiguration()
    {
        if (!ConfirmPending()) return;
        configuration = new RuleConfiguration { Groups = [new() { Name = "預設規則組", IsDefault = true }] }; BindGroups(); Touch();
    }
    private void OpenConfiguration()
    {
        if (!ConfirmPending()) return;
        using var dialog = new OpenFileDialog { Filter = "JSON 規則設定|*.json", FileName = configurationPath };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var opened = ConfigurationJson.Deserialize(File.ReadAllText(dialog.FileName)); ConfigurationValidator.Validate(opened);
        configuration = opened; configurationPath = dialog.FileName; dirty = false; BindGroups(); UpdateStatus();
    }
    private void Preview()
    {
        if (!groups.EndEdit() || !rules.EndEdit()) throw new InvalidOperationException("請先完成目前欄位的編輯。");
        ConfigurationValidator.Validate(configuration);
        var dialog = new PreviewForm(ConfigurationJson.Deserialize(ConfigurationJson.Serialize(configuration))); dialog.Show();
    }
    private void Export()
    {
        if (!Save()) return;
        using var dialog = new FolderBrowserDialog { Description = "選擇 COM 外掛匯出資料夾", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var result = PluginExporter.Export(configuration, configurationPath, AppPaths.Payload, dialog.SelectedPath, Application.ExecutablePath, AppPaths.Backups);
        status.Text = $"已匯出 {result.Manifest.Groups.Count} 個組到 {dialog.SelectedPath}";
    }
    private void Install()
    {
        WinMergeIntegration.EnsureArchitecture(); if (!Save()) return;
        var result = PluginExporter.Export(configuration, configurationPath, AppPaths.Payload, AppPaths.DefaultPlugins,
            Application.ExecutablePath, AppPaths.Backups, destinationInUse: WinMergeIntegration.IsRunning());
        if (!result.RulesOnlyUpdate) WinMergeIntegration.ConfigureAutomatic();
        var refresh = WinMergeRefresh.RefreshOpenWindows(result.ReloadPluginsRequired);
        status.Text = $"已套用 {result.Manifest.Groups.Count} 個組。" + refresh.Message;
        // 日常套用以狀態列提示，不增加阻擋編輯的確認視窗。
        if (!result.RulesOnlyUpdate)
            MessageBox.Show(this, $"已安裝 {result.Manifest.Groups.Count} 個組並啟用自動預差異化。\n\n{AppPaths.DefaultPlugins}\n{refresh.Message}", "已套用到 WinMerge");
    }
    private void UpdateStatus()
    {
        status.Text = $"{(dirty ? "已修改，尚未儲存" : "設定已載入")}　{configurationPath}　共 {configuration.Groups.Count} 組";
        Text = "EDI 預差異化規則管理器 — C# COM" + (dirty ? " *" : "");
    }
    protected override bool ProcessCmdKey(ref Message message, Keys keys)
    {
        if (keys == (Keys.Control | Keys.S)) { Run(() => Save()); return true; }
        if (keys == (Keys.Control | Keys.O)) { Run(OpenConfiguration); return true; }
        if (keys == (Keys.Control | Keys.T)) { Run(Preview); return true; }
        return base.ProcessCmdKey(ref message, keys);
    }
}
