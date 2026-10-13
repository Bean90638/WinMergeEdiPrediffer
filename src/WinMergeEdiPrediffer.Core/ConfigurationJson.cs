using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace WinMergeEdiPrediffer.Core;

public static class ConfigurationJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new RuleConfigurationConverter() }
    };

    public static string Serialize(RuleConfiguration configuration)
        => JsonSerializer.Serialize(configuration, Options);

    public static RuleConfiguration Deserialize(string json)
        => JsonSerializer.Deserialize<RuleConfiguration>(json, Options)
            ?? throw new JsonException("設定文件不可為 null。");

    // 同時涵蓋管理器設定與 COM 共用規則內的巢狀設定；舊順序只在載入 Schema 1 時轉換一次。
    private sealed class RuleConfigurationConverter : JsonConverter<RuleConfiguration>
    {
        public override RuleConfiguration Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var version = root.TryGetProperty("schemaVersion", out var schema) ? schema.GetInt32() : 1;
            var groups = root.TryGetProperty("groups", out var groupArray)
                ? groupArray.Deserialize<List<RuleGroup>>(options) : [];
            if (version == 1 && groups is not null && groupArray.ValueKind == JsonValueKind.Array)
            {
                var legacy = groupArray.EnumerateArray().Select(group => (
                    IsDefault: group.TryGetProperty("isDefault", out var fallback) && fallback.GetBoolean(),
                    Priority: group.TryGetProperty("priority", out var priority) ? priority.GetInt32() : 100)).ToArray();
                groups = groups.Select((group, index) => (Group: group, Legacy: legacy[index]))
                    .OrderBy(item => item.Legacy.IsDefault).ThenBy(item => item.Legacy.Priority)
                    .Select(item => item.Group).ToList();
            }
            return new RuleConfiguration { SchemaVersion = version == 1 ? 2 : version, Groups = groups! };
        }

        public override void Write(Utf8JsonWriter writer, RuleConfiguration value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", value.SchemaVersion);
            writer.WritePropertyName("groups");
            JsonSerializer.Serialize(writer, value.Groups, options);
            writer.WriteEndObject();
        }
    }
}
