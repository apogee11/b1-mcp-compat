using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace B1.Mcp.Compat.Core.Kb;

/// <summary>Annotation facts of one property as served by a native FP2608+ Service Layer.</summary>
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

    /// <summary>
    /// What the native by-table personal-data action returns for each table: "OPFSTable|pfsc_Category" pairs. Not derivable from OPFS
    /// alone (e.g. ORDR returns the sales rows of the abstract table ODOC), so it is learned from a native Service Layer by gen-kb.
    /// </summary>
    [JsonPropertyName("personal")] public Dictionary<string, List<string>> Personal { get; set; } = new(StringComparer.Ordinal);

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public Dictionary<string, KbProp?>? TypeOf(string set, string type) =>
        Sets.TryGetValue(set, out var s) && s.Types.TryGetValue(type, out var id) && Blobs.TryGetValue(id, out var b) ? b : null;

    /// <summary>Content hash used as blob id.</summary>
    public static string HashOf(Dictionary<string, KbProp?> props)
    {
        var sb = new StringBuilder();
        foreach (var kv in props.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            sb.Append(kv.Key).Append('|');
            if (kv.Value is null) sb.Append("NULL");
            else sb.Append(kv.Value.Label).Append('|').Append(kv.Value.Column).Append('|').Append(kv.Value.ChildTable);
            sb.Append('\n');
        }
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..12].ToLowerInvariant();
    }

    public string Intern(Dictionary<string, KbProp?> props)
    {
        var sorted = props.OrderBy(k => k.Key, StringComparer.Ordinal).ToDictionary(k => k.Key, k => k.Value, StringComparer.Ordinal);
        var id = HashOf(sorted);
        Blobs[id] = sorted;
        return id;
    }

    /// <summary>Union with <paramref name="newer"/>; where both know a fact, the newer wins (property level).</summary>
    public void MergeFrom(KnowledgeBase newer)
    {
        foreach (var (name, ns) in newer.Sets)
        {
            if (!Sets.TryGetValue(name, out var cur)) { cur = new KbSet(); Sets[name] = cur; }
            cur.Type = ns.Type;
            cur.Label = ns.Label ?? cur.Label;
            cur.Table = ns.Table ?? cur.Table;
            foreach (var (tn, id) in ns.Types)
            {
                var props = cur.Types.TryGetValue(tn, out var oid) && Blobs.TryGetValue(oid, out var ob)
                    ? new Dictionary<string, KbProp?>(ob, StringComparer.Ordinal)
                    : new Dictionary<string, KbProp?>(StringComparer.Ordinal);
                foreach (var (pn, pv) in newer.Blobs[id]) props[pn] = pv;
                cur.Types[tn] = Intern(props);
            }
        }
        foreach (var (en, members) in newer.Enums)
        {
            if (!Enums.TryGetValue(en, out var cur)) Enums[en] = cur = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var (m, v) in members) cur[m] = v;
        }
        foreach (var (t, pairs) in newer.Personal) Personal[t] = Personal.TryGetValue(t, out var cur) ? cur.Union(pairs).Order(StringComparer.Ordinal).ToList() : pairs;
        Sources.AddRange(newer.Sources);
        if (newer.GeneratedAt > GeneratedAt) GeneratedAt = newer.GeneratedAt;
        DropUnusedBlobs();
    }

    public void DropUnusedBlobs()
    {
        var used = Sets.Values.SelectMany(s => s.Types.Values).ToHashSet(StringComparer.Ordinal);
        foreach (var k in Blobs.Keys.Where(k => !used.Contains(k)).ToList()) Blobs.Remove(k);
    }

    public static KnowledgeBase Load(Stream s) =>
        JsonSerializer.Deserialize<KnowledgeBase>(s, Json) ?? throw new InvalidDataException("Empty knowledge base");

    public void Save(Stream s) => JsonSerializer.Serialize(s, this, Json);
}
