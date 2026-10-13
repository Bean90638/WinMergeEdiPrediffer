using System.Drawing.Imaging;
using System.Reflection;
using WinMergeEdiPrediffer.Core;
using WinMergeEdiPrediffer.Manager;

namespace WinMergeEdiPrediffer.Verification;

internal static class ManagerVerification
{
    internal static void Run(Action<bool, string> check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Verify(check); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Manager verification failed.", failure);
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static void Invoke(object owner, string method, params object[] arguments) =>
        owner.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, arguments);

    private static DragEventArgs Drag(RuleGridView source, int rowIndex, RuleGridView target, int targetRow, bool after)
    {
        var data = source.BeginRowDrag(rowIndex);
        try
        {
            var bounds = target.GetRowDisplayRectangle(targetRow, false);
            var point = target.PointToScreen(new Point(target.RowHeadersWidth / 2, after ? bounds.Bottom - 2 : bounds.Top + 2));
            var drag = new DragEventArgs(data, 1, point.X, point.Y, DragDropEffects.Move, DragDropEffects.None);
            Invoke(target, "OnDragOver", drag);
            Invoke(target, "OnDragDrop", drag);
            return drag;
        }
        finally { source.EndRowDrag(); }
    }

    private static void Snapshot(Form form, string path)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void Verify(Action<bool, string> check)
    {
        var root = Path.GetFullPath(Path.Combine("work", "manager-verification"));
        Directory.CreateDirectory(root);
        var configuration = new RuleConfiguration { Groups =
        [
            new() { Id = "specific", Name = "EDI 860", FilePattern = @"\\860\\.*\.edi$", Rules =
                [new() { Name = "X to Y", FindPattern = "X", Replacement = "Y" }, new() { Name = "Y to Z", FindPattern = "Y", Replacement = "Z" }] },
            new() { Id = "fallback", Name = "其他檔案" }
        ] };
        var file = Path.Combine(root, "profiles.json");
        File.WriteAllText(file, ConfigurationJson.Serialize(configuration));
        using var manager = new MainForm(file);
        manager.ShowInTaskbar = false; manager.Opacity = 0;
        manager.Show();
        manager.PerformLayout();
        var groups = Field<RuleGridView>(manager, "groups");
        var rules = Field<RuleGridView>(manager, "rules");
        _ = groups.Handle; _ = rules.Handle;
        check(groups.Columns.Count == 3 && !groups.Columns.Contains("Priority") && !groups.Columns.Contains("IsDefault"), "manager uses display order without priority or default columns");
        groups.CurrentCell = groups.Rows[0].Cells[nameof(RuleGroup.FilePattern)];
        var clipboard = groups.GetClipboardContent()!;
        check(clipboard.TryGetData<string>(DataFormats.UnicodeText, out var copiedPath) && copiedPath == configuration.Groups[0].FilePattern
            && !clipboard.GetDataPresent(DataFormats.Html, false) && !clipboard.GetDataPresent(DataFormats.CommaSeparatedValue, false), "path regex copies exact plain text without table or headers");
        rules.CurrentCell = rules.Rows[1].Cells[nameof(ReplacementRule.FindPattern)];
        check(rules.GetClipboardContent()!.TryGetData<string>(DataFormats.UnicodeText, out var copiedFind) && copiedFind == "Y", "rule copy contains only current find cell");
        groups.CurrentCell = groups.Rows[0].Cells[nameof(RuleGroup.Name)];
        check(rules.CurrentRow?.Index == 1, "changing group cell keeps the selected rule");
        var bound = Field<RuleConfiguration>(manager, "configuration");
        var drag = Drag(groups, 0, groups, 1, true);
        check(drag.Effect == DragDropEffects.Move && bound.Groups[1].Id == "specific"
            && groups.CurrentRow?.Index == 1 && RuleEngine.SelectGroup(bound, @"D:\860\test.edi")?.Id == "fallback", "group drop downward updates routing and preserves moved selection");
        Drag(groups, 1, groups, 0, false);
        check(bound.Groups[0].Id == "specific" && RuleEngine.SelectGroup(bound, @"D:\860\test.edi")?.Id == "specific", "group drop upward restores matching order");
        Drag(rules, 1, rules, 0, false);
        check(bound.Groups[0].Rules[0].Name == "Y to Z" && RuleEngine.Apply(bound.Groups[0], "X") == "Y"
            && Field<TextBox>(manager, "find").Text == "Y", "rule drop changes execution order and keeps editor on moved rule");
        File.WriteAllText(file, ConfigurationJson.Serialize(bound));
        using (var reopened = new MainForm(file))
        {
            var reloaded = Field<RuleConfiguration>(reopened, "configuration");
            check(reloaded.Groups.Select(g => g.Id).SequenceEqual(bound.Groups.Select(g => g.Id))
                && RuleEngine.Apply(reloaded.Groups[0], "X") == "Y", "dragged group and rule order persist after saving and reopening");
        }
        Drag(rules, 0, rules, 1, true);
        check(RuleEngine.Apply(bound.Groups[0], "X") == "Z", "rule drop downward executes every enabled rule in displayed order");
        check(Drag(groups, 0, rules, 0, false).Effect == DragDropEffects.None && bound.Groups.Count == 2, "cross-grid drop is rejected");
        Snapshot(manager, Path.Combine(root, "manager.png"));
    }
}
