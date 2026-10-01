using System.Text;
using System.Text.Json;

namespace B1.Mcp.Compat.Core.Metadata;

/// <summary>Something the connector could not resolve. Kind: entity | property | enum.</summary>
public sealed record UnresolvedItem(string Kind, string Entity, string? Type, string Name, string? Table);

/// <summary>The entity cannot be annotated faithfully. Always a failure (never guessed); IT fixes it in mappings.override.json.</summary>
public sealed class UnresolvedMappingException : Exception
{
    public string Entity { get; }
    public IReadOnlyList<UnresolvedItem> Items { get; }

    public UnresolvedMappingException(string entity, IReadOnlyList<UnresolvedItem> items) : base(BuildMessage(entity, items))
    {
        Entity = entity;
        Items = items;
    }

    private static string BuildMessage(string entity, IReadOnlyList<UnresolvedItem> items)
    {
        const int max = 15;
        var sb = new StringBuilder();
        sb.Append($"b1-mcp-compat cannot describe entity '{entity}': {items.Count} item(s) could not be resolved from the knowledge base or the company database. ");
        foreach (var i in items.Take(max))
        {
            sb.Append(i.Kind switch
            {
                "entity" => "[entity set] ",
                "enum" => $"[enum {i.Type}] member '{i.Name}' ",
                _ => $"[property] '{i.Name}' (type {i.Type}{(i.Table != null ? ", table " + i.Table : "")}) ",
            });
        }
        if (items.Count > max) sb.Append($"... and {items.Count - max} more. ");
        var first = items[0];
        sb.Append("If the Service Layer build differs from the one of the knowledge base, see compat/CHECK-YOUR-BUILD.md. Otherwise ask IT to add a line to mappings.override.json, e.g. ");
        sb.Append(first.Kind switch
        {
            "entity" => $"{{\"entity\":\"{entity}\",\"table\":\"<DB table>\",\"label\":\"<optional>\"}}",
            "enum" => $"{{\"enums\":[{{\"enum\":\"{first.Type}\",\"member\":\"{first.Name}\",\"value\":\"<stored value>\"}}]}}",
            _ => $"{{\"entity\":\"{entity}\",\"property\":\"{first.Name}\",\"table\":\"{first.Table ?? "<DB table>"}\",\"column\":\"<DB column>\",\"label\":\"<optional>\"}}",
        });
        sb.Append('.');
        return sb.ToString();
    }

    /// <summary>Service Layer style error body, with the unresolved items for tooling.</summary>
    public string ToJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteStartObject("error");
            w.WriteNumber("code", -1);
            w.WriteStartObject("message");
            w.WriteString("lang", "en-us");
            w.WriteString("value", Message);
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteString("compat", "b1-mcp-compat");
            w.WriteString("entity", Entity);
            w.WriteStartArray("unresolved");
            foreach (var i in Items)
            {
                w.WriteStartObject();
                w.WriteString("kind", i.Kind);
                w.WriteString("type", i.Type);
                w.WriteString("name", i.Name);
                w.WriteString("table", i.Table);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}

/// <summary>The requested entity set does not exist in the Service Layer model.</summary>
public sealed class EntitySetNotFoundException : Exception
{
    public EntitySetNotFoundException(string name) : base($"Entity set '{name}' not found") { }
}
