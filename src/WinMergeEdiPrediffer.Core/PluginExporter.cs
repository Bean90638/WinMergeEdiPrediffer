using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace WinMergeEdiPrediffer.Core;

public sealed class ExportManifest
{
    public string Product { get; set; } = "WinMergeEdiPrediffer";
    public string GenerationId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public string ConfigurationPath { get; set; } = "";
    public bool SupportsLiveUpdates { get; set; }
    public bool PathRouting { get; set; }
    public string PayloadSignature { get; set; } = "";
    public List<string> OwnedFiles { get; set; } = [];
    public List<ExportedGroup> Groups { get; set; } = [];
    // 保留舊安裝清單的欄位，升級時才能備份及清除各組的獨立外掛。
    public List<ExportedGroup> RetiredGroups { get; set; } = [];
}

public sealed record ExportedGroup(string Id, string Name, string PluginName, string EffectiveFilter);
public sealed record ExportResult(ExportManifest Manifest, string? BackupDirectory,
    bool RulesOnlyUpdate = false, bool ReloadPluginsRequired = true);

public static class PluginExporter
{
    public const string ManifestFilename = "EdiPrediff.install.json";
    public const string PayloadName = "WinMergeEdiPrediffer.Plugin";
    public const string LiveRulesFilename = "EdiPrediff.rules.json";
    public const string RouterName = "EdiPrediff_Router";
    private static readonly string[] PayloadExtensions = [".bridge.dll", ".comhost.dll", ".dll", ".runtimeconfig.json", ".deps.json"];
    private static readonly string[] RouterFiles =
    [
        RouterName + "/" + PayloadName + ".comhost.dll", RouterName + "/" + PayloadName + ".dll",
        RouterName + "/" + PayloadName + ".runtimeconfig.json", RouterName + "/" + PayloadName + ".deps.json",
        RouterName + "/" + PayloadName + ".profile.json", RouterName + ".comhost.dll"
    ];

    // 僅用於讀取舊 Schema 2 profile；新版路徑選組不交給 WinMerge 篩選器。
    public static string EffectiveFilter(RuleGroup group, IEnumerable<RuleGroup> previous)
    {
        var exclusions = previous.Select(g => "(?:" + g.FilePattern + ")").ToArray();
        var exclude = exclusions.Length == 0 ? "" : "(?!.*(?:" + string.Join("|", exclusions) + "))";
        return "^" + exclude + "(?=.*(?:" + group.FilePattern + ")).*$";
    }

    public static ExportResult Export(RuleConfiguration configuration, string configurationPath,
        string payloadDirectory, string destinationDirectory, string managerPath, string backupRoot,
        bool destinationInUse = false)
    {
        ConfigurationValidator.Validate(configuration);
        var destination = Path.GetFullPath(destinationDirectory);
        var payload = Path.GetFullPath(payloadDirectory);
        var manifestPath = Path.Combine(destination, ManifestFilename);
        var old = File.Exists(manifestPath)
            ? JsonSerializer.Deserialize<ExportManifest>(File.ReadAllText(manifestPath), ConfigurationJson.Options)
                ?? throw new InvalidOperationException("安裝清單不可為 null。") : null;
        if (old is not null && old.Product != "WinMergeEdiPrediffer") throw new InvalidOperationException("目標資料夾有其他產品的同名安裝清單。");
        if (old is not null) foreach (var file in old.OwnedFiles) _ = ResolveOwnedFile(destination, file);
        foreach (var extension in PayloadExtensions)
            if (!File.Exists(Path.Combine(payload, PayloadName + extension))) throw new FileNotFoundException("缺少 COM 外掛部署檔。請先完整建置專案。", PayloadName + extension);
        var signature = PayloadSignature(payload);
        var manifest = new ExportManifest
        {
            ConfigurationPath = Path.GetFullPath(configurationPath), SupportsLiveUpdates = true,
            PathRouting = true, PayloadSignature = signature, OwnedFiles = [LiveRulesFilename, .. RouterFiles],
            Groups = RuleEngine.OrderedGroups(configuration).Select(g => new ExportedGroup(g.Id, g.Name, RouterName, g.FilePattern)).ToList()
        };
        if (old is not null && IsLiveDeployment(old, destination, payload, signature))
        {
            // 新增／移除組、路徑與優先序變更也只發布 JSON；固定入口與外掛資訊不變。
            var backup = Backup(old, destination, backupRoot, manifest.GenerationId);
            var rulesPath = Path.Combine(destination, LiveRulesFilename);
            var oldRules = File.ReadAllText(rulesPath);
            try
            {
                AtomicWrite(rulesPath, LiveRules(configuration, configurationPath, managerPath));
                AtomicWrite(manifestPath, JsonSerializer.Serialize(manifest, ConfigurationJson.Options));
            }
            catch { AtomicWrite(rulesPath, oldRules); throw; }
            return new(manifest, backup, RulesOnlyUpdate: true, ReloadPluginsRequired: false);
        }
        if (destinationInUse && old is not null)
            throw new InvalidOperationException("這次需要升級或修復 COM 外掛程式，請關閉 WinMerge 後再套用一次。升級完成後，日常修改規則不必關閉 WinMerge 或擴充功能視窗。");

        var stage = Path.Combine(Path.GetTempPath(), "WinMergeEdiPrediffer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(stage, RouterName));
        string? fullBackup = null;
        var copied = new List<string>();
        var existingBefore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var extension in PayloadExtensions.Where(e => e != ".bridge.dll"))
                File.Copy(Path.Combine(payload, PayloadName + extension), Path.Combine(stage, RouterName, PayloadName + extension));
            var profile = new PluginProfile
            {
                SchemaVersion = 3, SharedRulesPath = "../" + LiveRulesFilename,
                Group = new RuleGroup { Id = "router", Name = "EDI 預差異器" }, FileFilter = ".*",
                ConfigurationPath = manifest.ConfigurationPath, ManagerPath = Path.GetFullPath(managerPath),
                ComHostPath = PayloadName + ".comhost.dll"
            };
            File.WriteAllText(Path.Combine(stage, RouterName, PayloadName + ".profile.json"), JsonSerializer.Serialize(profile, ConfigurationJson.Options));
            File.WriteAllText(Path.Combine(stage, LiveRulesFilename), LiveRules(configuration, configurationPath, managerPath));
            File.Copy(Path.Combine(payload, PayloadName + ".bridge.dll"), Path.Combine(stage, RouterName + ".comhost.dll"));
            foreach (var file in manifest.OwnedFiles)
            {
                var target = ResolveOwnedFile(destination, file);
                if (File.Exists(target) && (old is null || !old.OwnedFiles.Contains(file, StringComparer.OrdinalIgnoreCase)))
                    throw new IOException("目標已有不在本專案安裝清單內的檔案，請換一個匯出資料夾：" + target);
            }
            if (old is not null)
            {
                foreach (var file in old.OwnedFiles)
                {
                    var target = ResolveOwnedFile(destination, file);
                    if (File.Exists(target))
                    {
                        using var check = File.Open(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                        existingBefore.Add(file);
                    }
                }
                fullBackup = Backup(old, destination, backupRoot, manifest.GenerationId);
            }
            Directory.CreateDirectory(destination);
            // 最後放入最上層 DLL，避免掃描到尚未準備好載荷及共用規則的入口。
            foreach (var file in manifest.OwnedFiles)
            {
                var target = ResolveOwnedFile(destination, file);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                // 先記錄寫入嘗試，複製中途失敗也能清除新建的檔案。
                copied.Add(file);
                File.Copy(ResolveOwnedFile(stage, file), target, true);
            }
            if (old is not null)
                foreach (var file in old.OwnedFiles.Except(manifest.OwnedFiles, StringComparer.OrdinalIgnoreCase))
                    File.Delete(ResolveOwnedFile(destination, file));
            AtomicWrite(manifestPath, JsonSerializer.Serialize(manifest, ConfigurationJson.Options));
            return new(manifest, fullBackup);
        }
        catch
        {
            foreach (var file in copied)
                if (!existingBefore.Contains(file))
                    File.Delete(ResolveOwnedFile(destination, file));
            if (fullBackup is not null && old is not null)
                foreach (var file in old.OwnedFiles)
                {
                    var source = ResolveOwnedFile(fullBackup, file);
                    if (File.Exists(source)) File.Copy(source, ResolveOwnedFile(destination, file), true);
                }
            throw;
        }
        finally
        {
            // 只清理本次 UUID 暫存目錄裡明確列出的檔案，保留使用者的安裝資料夾。
            foreach (var file in manifest.OwnedFiles)
            {
                var path = ResolveOwnedFile(stage, file);
                if (File.Exists(path)) File.Delete(path);
            }
            foreach (var directory in Directory.GetDirectories(stage))
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            if (!Directory.EnumerateFileSystemEntries(stage).Any()) Directory.Delete(stage);
        }
    }

    private static bool IsLiveDeployment(ExportManifest manifest, string destination, string payload, string signature)
    {
        if (!manifest.SupportsLiveUpdates || !manifest.PathRouting || manifest.PayloadSignature != signature ||
            !manifest.OwnedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(RouterFiles.Append(LiveRulesFilename)) ||
            !File.Exists(Path.Combine(destination, LiveRulesFilename))) return false;
        if (!FilesEqual(Path.Combine(destination, RouterName + ".comhost.dll"), Path.Combine(payload, PayloadName + ".bridge.dll"))) return false;
        foreach (var extension in PayloadExtensions.Where(e => e != ".bridge.dll"))
            if (!FilesEqual(Path.Combine(destination, RouterName, PayloadName + extension), Path.Combine(payload, PayloadName + extension))) return false;
        var profilePath = Path.Combine(destination, RouterName, PayloadName + ".profile.json");
        if (!File.Exists(profilePath)) return false;
        var profile = JsonSerializer.Deserialize<PluginProfile>(File.ReadAllText(profilePath), ConfigurationJson.Options);
        return profile?.SchemaVersion == 3 && profile.SharedRulesPath == "../" + LiveRulesFilename && profile.ComHostPath == PayloadName + ".comhost.dll";
    }

    private static string LiveRules(RuleConfiguration configuration, string configurationPath, string managerPath) =>
        JsonSerializer.Serialize(new LivePluginConfiguration
        {
            Configuration = configuration, ConfigurationPath = Path.GetFullPath(configurationPath), ManagerPath = Path.GetFullPath(managerPath)
        }, ConfigurationJson.Options);

    private static string Backup(ExportManifest manifest, string destination, string backupRoot, string generation)
    {
        var backup = Path.Combine(Path.GetFullPath(backupRoot), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + generation);
        Directory.CreateDirectory(backup);
        foreach (var file in manifest.OwnedFiles)
        {
            var source = ResolveOwnedFile(destination, file);
            if (!File.Exists(source)) continue;
            var target = ResolveOwnedFile(backup, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }
        File.Copy(Path.Combine(destination, ManifestFilename), Path.Combine(backup, ManifestFilename));
        return backup;
    }

    private static string PayloadSignature(string payload) => string.Join("|", PayloadExtensions.Select(extension =>
        extension + ":" + Hash(Path.Combine(payload, PayloadName + extension))));
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static bool FilesEqual(string first, string second) => File.Exists(first) && Hash(first) == Hash(second);

    public static void AtomicWrite(string path, string text)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, text); File.Move(temporary, fullPath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string ResolveOwnedFile(string root, string relative)
    {
        if (relative != LiveRulesFilename && !Regex.IsMatch(relative, @"^EdiPrediff_[A-Za-z0-9_-]+(?:\.(?:comhost\.dll|dll|runtimeconfig\.json|deps\.json)|/WinMergeEdiPrediffer\.Plugin\.(?:comhost\.dll|dll|runtimeconfig\.json|deps\.json|profile\.json))$"))
            throw new InvalidOperationException("安裝清單含有不屬於本專案的路徑：" + relative);
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("安裝清單的路徑超出目標資料夾。");
        return path;
    }
}
