using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace WinMergeEdiPrediffer.Core;

public static class ConfigurationJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public static string Serialize(RuleConfiguration configuration)
        => JsonSerializer.Serialize(configuration, Options);

    public static RuleConfiguration Deserialize(string json)
        => JsonSerializer.Deserialize<RuleConfiguration>(json, Options)
            ?? throw new JsonException("設定文件不可為 null。");
}
