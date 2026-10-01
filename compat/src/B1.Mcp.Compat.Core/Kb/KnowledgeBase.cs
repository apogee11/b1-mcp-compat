using System.Text.Json;
using System.Text.Json.Serialization;

namespace B1.Mcp.Compat.Core.Kb;

/// <summary>Annotation facts of one property (what a native FP2608+ Service Layer serves).</summary>
public sealed class KbProp
{
    [JsonPropertyName("l"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Label { get; set; }
    [JsonPropertyName("c"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Column { get; set; }
    [JsonPropertyName("t"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ChildTable { get; set; }
}

/// <summary>Entity set level facts. Structural types are referenced by blob id into <see cref="KnowledgeBase.Blobs"/>.</summary>
public sealed class KbSet
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("table")] public string? Table { get; set; }

    /// <summary>Structural type name to blob id. Only SAP-standard knowledge (no user-defined fields).</summary>
    [JsonPropertyName("types")] public Dictionary<string, string> Types { get; set; } = new(StringComparer.Ordinal);
}

public sealed class KbSource
{
    [JsonPropertyName("sl")] public string? ServiceLayer { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("at")] public DateTimeOffset At { get; set; }
}

/// <summary>
/// Knowledge base: SAP-standard knowledge of what is behind standard entities and fields (label, table, column, child table,
/// enum valid values). A property whose value is null exists natively but is NOT annotated (the native scoped metadata omits
/// it); that differs from a property absent from the KB (unknown). Never holds company-specific things
/// (user fields, user tables, user objects).
/// </summary>
public sealed class KnowledgeBase
{
    [JsonPropertyName("format")] public int Format { get; set; } = 2;
    [JsonPropertyName("generatedAt")] public DateTimeOffset GeneratedAt { get; set; }
    [JsonPropertyName("sources")] public List<KbSource> Sources { get; set; } = new();
    [JsonPropertyName("sets")] public Dictionary<string, KbSet> Sets { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Property blobs (shared between entity sets): property name to annotation or null.</summary>
    [JsonPropertyName("blobs")] public Dictionary<string, Dictionary<string, KbProp?>> Blobs { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Enum type to member to SAPB1.ValidValue (null when the member has none). Identical in every entity set.</summary>
    [JsonPropertyName("enums")] public Dictionary<string, Dictionary<string, string?>> Enums { get; set; } = new(StringComparer.Ordinal);

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public Dictionary<string, KbProp?>? TypeOf(string set, string type) =>
        Sets.TryGetValue(set, out var s) && s.Types.TryGetValue(type, out var id) && Blobs.TryGetValue(id, out var b) ? b : null;

    public static KnowledgeBase Load(Stream s) =>
        JsonSerializer.Deserialize<KnowledgeBase>(s, Json) ?? throw new InvalidDataException("Empty knowledge base");

}
