using System.Text.Json;
using System.Text.Json.Serialization;

namespace B1.Mcp.Compat.Core.Mappings;

/// <summary>
/// Manual mappings written by IT in mappings.override.json for things the connector cannot resolve by itself.
/// <code>
/// { "mappings": [
///     { "entity": "Orders", "property": "SomeField", "table": "ORDR", "column": "SomeCol", "label": "Optional label" },
///     { "entity": "SomeEntity", "table": "OXYZ", "label": "Optional entity label" } ],
///   "enums": [ { "enum": "BoSomeEnum", "member": "bo_X", "value": "X" } ] }
/// </code>
/// A property entry applies to the property named <c>property</c> of the structural type of <c>entity</c> that is stored in
/// <c>table</c> (header table for the root type, the child table for collections). <c>childTable</c> may be given for
/// complex properties. An entry without <c>property</c> maps the entity set itself.
/// </summary>
public sealed class MappingOverrides
{
    public sealed class Mapping
    {
        [JsonPropertyName("entity")] public string Entity { get; set; } = "";
        [JsonPropertyName("property")] public string? Property { get; set; }
        [JsonPropertyName("table")] public string? Table { get; set; }
        [JsonPropertyName("column")] public string? Column { get; set; }
        [JsonPropertyName("label")] public string? Label { get; set; }
        [JsonPropertyName("childTable")] public string? ChildTable { get; set; }
    }

    public sealed class EnumMapping
    {
        [JsonPropertyName("enum")] public string Enum { get; set; } = "";
        [JsonPropertyName("member")] public string Member { get; set; } = "";
        [JsonPropertyName("value")] public string Value { get; set; } = "";
    }

    [JsonPropertyName("mappings")] public List<Mapping> Mappings { get; set; } = new();
    [JsonPropertyName("enums")] public List<EnumMapping> Enums { get; set; } = new();

    public static readonly MappingOverrides Empty = new();

    private static readonly JsonSerializerOptions Opt = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static MappingOverrides Parse(string json) => JsonSerializer.Deserialize<MappingOverrides>(json, Opt) ?? Empty;

    public Mapping? FindProperty(string entity, string property, string? table) =>
        Mappings.FirstOrDefault(m => m.Property != null
            && string.Equals(m.Entity, entity, StringComparison.Ordinal)
            && string.Equals(m.Property, property, StringComparison.Ordinal)
            && (table == null || m.Table == null || string.Equals(m.Table, table, StringComparison.OrdinalIgnoreCase)));

    public Mapping? FindEntity(string entity) =>
        Mappings.FirstOrDefault(m => m.Property == null && string.Equals(m.Entity, entity, StringComparison.Ordinal));

    public string? FindEnumValue(string enumName, string member) =>
        Enums.FirstOrDefault(e => e.Enum == enumName && e.Member == member)?.Value;
}
