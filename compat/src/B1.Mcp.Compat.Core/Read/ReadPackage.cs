using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace B1.Mcp.Compat.Core.Read;

/// <summary>JSON side of a column: property name, EDM type and enum name (the whole object is absent when the column is not exposed).</summary>
public sealed class ReadColumnJson
{
    [JsonPropertyName("property")] public string Property { get; set; } = "";
    [JsonPropertyName("edmType")] public string? EdmType { get; set; }
    [JsonPropertyName("enum")] public string? Enum { get; set; }
}

public sealed class ReadColumn
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("sqlType")] public string SqlType { get; set; } = "";
    [JsonPropertyName("nullable")] public bool Nullable { get; set; }
    [JsonPropertyName("json")] public ReadColumnJson? Json { get; set; }

    /// <summary>Set by the generator when the SQL type was not derived from the engine (e.g. "sqltype-M"): touching the column is an error, never a guess.</summary>
    [JsonPropertyName("sqlTypePending")] public string? SqlTypePending { get; set; }

    /// <summary>SQL type without length/precision ("nvarchar(40)" is "nvarchar").</summary>
    public string BaseType => SqlType.Contains('(') ? SqlType[..SqlType.IndexOf('(')] : SqlType;
}

public sealed class ReadJoin
{
    [JsonPropertyName("childColumn")] public string ChildColumn { get; set; } = "";
    [JsonPropertyName("parentColumn")] public string ParentColumn { get; set; } = "";
}

/// <summary>Child table of a set (a collection of the service, read with one extra query per root row).</summary>
public sealed class ReadChild
{
    [JsonPropertyName("collection")] public string Collection { get; set; } = "";
    [JsonPropertyName("entityType")] public string EntityType { get; set; } = "";
    [JsonPropertyName("table")] public string Table { get; set; } = "";
    [JsonPropertyName("join")] public List<ReadJoin>? Join { get; set; }
    [JsonPropertyName("orderBy")] public List<string>? OrderBy { get; set; }
    [JsonPropertyName("columns")] public List<ReadColumn> Columns { get; set; } = new();

    /// <summary>JSON property names in EntityType order. Optional in the file; JSON output needs it.</summary>
    [JsonPropertyName("propertyOrder")] public List<string>? PropertyOrder { get; set; }

    /// <summary>Generator marker: join/order not decided. Reading this child is an error.</summary>
    [JsonPropertyName("pending")] public JsonElement? Pending { get; set; }
}

public sealed class ReadRule
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("column")] public string? Column { get; set; }
}

public sealed class ReadSet
{
    [JsonPropertyName("entityType")] public string EntityType { get; set; } = "";
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("table")] public string Table { get; set; } = "";
    [JsonPropertyName("primaryKey")] public List<string> PrimaryKey { get; set; } = new();
    [JsonPropertyName("aliases")] public List<string> Aliases { get; set; } = new();

    /// <summary>Physical order of the table.</summary>
    [JsonPropertyName("columns")] public List<ReadColumn> Columns { get; set; } = new();
    [JsonPropertyName("omittedColumns")] public List<string> OmittedColumns { get; set; } = new();
    [JsonPropertyName("children")] public List<ReadChild> Children { get; set; } = new();
    [JsonPropertyName("rules")] public List<ReadRule> Rules { get; set; } = new();

    /// <summary>JSON property names in EntityType order. Optional in the file; JSON output needs it.</summary>
    [JsonPropertyName("propertyOrder")] public List<string>? PropertyOrder { get; set; }

    public bool HasRule(string name) => Rules.Any(r => r.Name == name);
    public ReadColumn? ColumnByName(string name) => Columns.FirstOrDefault(c => c.Name == name);
}

public sealed class ReadUnavailable
{
    [JsonPropertyName("table")] public string Table { get; set; } = "";
    [JsonPropertyName("missingTables")] public List<string> MissingTables { get; set; } = new();
    /// <summary>Optional: "key-column-missing" when the table exists but lacks a column of the set's key (so the set's key differs in this build); absent = "table-missing".</summary>
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonPropertyName("missingColumns")] public List<string> MissingColumns { get; set; } = new();
}

public sealed class ReadBuildInfo
{
    [JsonPropertyName("package")] public string Package { get; set; } = "";
    [JsonPropertyName("buildId")] public string BuildId { get; set; } = "";
    [JsonPropertyName("loginVersion")] public string LoginVersion { get; set; } = "";
}

/// <summary>Property of an EDM type of the package: type, nullability (only <c>false</c> is stored) and the annotation facts of the native $metadata (label, column, child table).</summary>
public sealed class ReadEdmProperty
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("nullable")] public bool? Nullable { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("column")] public string? Column { get; set; }
    [JsonPropertyName("childTable")] public string? ChildTable { get; set; }
}

/// <summary>An EntityType ("entity", with key) or ComplexType ("complex") of the extended sets, restricted to what the build serves.</summary>
public sealed class ReadEdmType
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("openType")] public bool OpenType { get; set; }
    [JsonPropertyName("hasStream")] public bool HasStream { get; set; }
    [JsonPropertyName("key")] public List<string>? Key { get; set; }
    [JsonPropertyName("properties")] public List<ReadEdmProperty> Properties { get; set; } = new();

    /// <summary>Enum types of properties the native scoped $metadata leaves out but whose enum it still pulls in with dependency=true.</summary>
    [JsonPropertyName("pullsEnums")] public List<string>? PullsEnums { get; set; }
}

public sealed class ReadEdmMember
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("value")] public int Value { get; set; }
    [JsonPropertyName("validValue")] public string? ValidValue { get; set; }
}

/// <summary>The $metadata side of the package (schemaVersion 2): types and enums of the served sets, from the native 2608 capture.</summary>
public sealed class ReadEdm
{
    [JsonPropertyName("types")] public Dictionary<string, ReadEdmType> Types { get; set; } = new(StringComparer.Ordinal);
    [JsonPropertyName("enums")] public Dictionary<string, List<ReadEdmMember>> Enums { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>What a name resolves to in a package: a served set, an unavailable one (table missing in this build) or nothing.</summary>
public sealed record ReadResolved(ReadSet? Set, ReadUnavailable? Unavailable);

/// <summary>
/// Extended read package of one build (<c>read-&lt;build&gt;.json.gz</c>, schemaVersion 1..2; format in rea/kbgen/readpkg-schema.md).
/// The runtime knows no set and no build: everything comes from here. Every identifier that reaches SQL is validated at load.
/// </summary>
public sealed class ReadPackage
{
    public const int MinSchemaVersion = 1;
    public const int MaxSchemaVersion = 2;

    /// <summary>Named rules the runtime implements; a package with another name is rejected (fail closed).</summary>
    public static readonly string[] KnownRules =
        { "enum-fuera-de-lista-omitir", "clave-con-fecha", "clave-compuesta-enum-select-400", "smallint-clave-trunca", "tablas-extra-leidas" };

    private static readonly Regex Ident = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonPropertyName("build")] public ReadBuildInfo Build { get; set; } = new();
    [JsonPropertyName("enums")] public Dictionary<string, Dictionary<string, string>> Enums { get; set; } = new(StringComparer.Ordinal);

    /// <summary>EDM of the served sets (schemaVersion 2). Without it the package serves data but announces nothing in $metadata.</summary>
    [JsonPropertyName("edm")] public ReadEdm? Edm { get; set; }
    /// <summary>How the build's serializer writes a date: absent = "yyyy-mm-ddT00:00:00Z" (2202 onwards); "date-only" = "yyyy-mm-dd" (builds whose ServiceLayer.dll has no such suffix, e.g. FP2108).</summary>
    [JsonPropertyName("dateJson")] public string? DateJson { get; set; }
    [JsonPropertyName("sets")] public Dictionary<string, ReadSet> Sets { get; set; } = new(StringComparer.Ordinal);
    [JsonPropertyName("unavailable")] public Dictionary<string, ReadUnavailable> Unavailable { get; set; } = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);

    /// <summary>True when <paramref name="id"/> is safe to put between brackets in SQL.</summary>
    public static bool IsSafeIdentifier(string id) => Ident.IsMatch(id);

    public static ReadPackage LoadGz(Stream gz)
    {
        using var z = new GZipStream(gz, CompressionMode.Decompress);
        return Load(z);
    }

    public static ReadPackage Load(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var v = doc.RootElement.TryGetProperty("schemaVersion", out var sv) && sv.ValueKind == JsonValueKind.Number ? sv.GetInt32() : -1;
        if (v < MinSchemaVersion || v > MaxSchemaVersion)
            throw new InvalidDataException($"Read package schemaVersion {(v < 0 ? "missing" : v)} is not supported (this connector reads schemaVersion {MinSchemaVersion} to {MaxSchemaVersion}).");
        var p = doc.RootElement.Deserialize<ReadPackage>() ?? throw new InvalidDataException("Empty read package.");
        p.Validate();
        return p;
    }

    private void Validate()
    {
        if (DateJson != null && DateJson != "date-only") throw new InvalidDataException($"Read package: unknown dateJson '{DateJson}'.");
        foreach (var (name, s) in Sets)
        {
            Check(name, s.Table);
            foreach (var c in s.Columns) Check(name, c.Name);
            foreach (var k in s.PrimaryKey)
                if (s.ColumnByName(k) == null) throw new InvalidDataException($"Read package: primary key column '{k}' of '{name}' is not a column of the table.");
            foreach (var r in s.Rules)
                if (!KnownRules.Contains(r.Name)) throw new InvalidDataException($"Read package: set '{name}' has the unknown rule '{r.Name}'.");
            foreach (var ch in s.Children)
            {
                Check(name, ch.Table);
                foreach (var c in ch.Columns) Check(name, c.Name);
                foreach (var o in ch.OrderBy ?? new())
                    if (ch.Columns.All(c => c.Name != o)) throw new InvalidDataException($"Read package: order column '{o}' of child '{ch.Collection}' is not a column.");
                foreach (var j in ch.Join ?? new())
                    if (ch.Columns.All(c => c.Name != j.ChildColumn) || s.ColumnByName(j.ParentColumn) == null)
                        throw new InvalidDataException($"Read package: join of child '{ch.Collection}' names an unknown column.");
            }
            foreach (var a in s.Aliases) if (!Sets.ContainsKey(a)) _aliases[a] = name;
        }
        foreach (var (name, u) in Unavailable)
        {
            Check(name, u.Table);
            if (u.Reason != null && u.Reason != "table-missing" && u.Reason != "key-column-missing") throw new InvalidDataException($"Read package: unknown unavailable reason '{u.Reason}' (set '{name}').");
        }
        if (Edm != null) ValidateEdm();
    }

    /// <summary>Every set (and child collection) names an EDM type that exists, and every type a property names is defined (or is Edm.*): a half-described EDM would produce a broken $metadata.</summary>
    private void ValidateEdm()
    {
        var edm = Edm!;
        string Short(string t)
        {
            if (t.StartsWith("Collection(", StringComparison.Ordinal) && t.EndsWith(')')) t = t[11..^1];
            return t.StartsWith("SAPB1.", StringComparison.Ordinal) ? t[6..] : t;
        }
        foreach (var (tn, t) in edm.Types)
        {
            Check(tn, tn);
            if (t.Kind is not ("entity" or "complex")) throw new InvalidDataException($"Read package: EDM type '{tn}' has the unknown kind '{t.Kind}'.");
            if (t.Kind == "entity" && (t.Key == null || t.Key.Count == 0 || t.Key.Any(k => t.Properties.All(p => p.Name != k))))
                throw new InvalidDataException($"Read package: EDM entity type '{tn}' has no valid key.");
            foreach (var p in t.Properties)
            {
                var pt = Short(p.Type);
                if (!pt.StartsWith("Edm.", StringComparison.Ordinal) && !edm.Types.ContainsKey(pt) && !edm.Enums.ContainsKey(pt))
                    throw new InvalidDataException($"Read package: property '{p.Name}' of EDM type '{tn}' names the undefined type '{p.Type}'.");
            }
            foreach (var en in t.PullsEnums ?? new())
                if (!edm.Enums.ContainsKey(en)) throw new InvalidDataException($"Read package: EDM type '{tn}' pulls the undefined enum '{en}'.");
        }
        foreach (var (name, s) in Sets)
            foreach (var tn in new[] { s.EntityType }.Concat(s.Children.Select(c => c.EntityType)))
                if (!edm.Types.ContainsKey(tn)) throw new InvalidDataException($"Read package: set '{name}' uses the EDM type '{tn}', which the package does not define.");
    }

    private static void Check(string set, string id)
    {
        if (!Ident.IsMatch(id)) throw new InvalidDataException($"Read package: '{id}' (set '{set}') is not a valid SQL identifier.");
    }

    public ReadResolved Resolve(string name)
    {
        if (Sets.TryGetValue(name, out var s)) return new(s, null);
        if (_aliases.TryGetValue(name, out var real)) return new(Sets[real], null);
        return Unavailable.TryGetValue(name, out var u) ? new(null, u) : new(null, null);
    }

    /// <summary>Every name the set <paramref name="name"/> is known by in this package (its own name, the real name when it is an alias, and all its aliases). Just <paramref name="name"/> for an unavailable or unknown one.</summary>
    public IReadOnlyList<string> NamesOf(string name)
    {
        var set = Resolve(name).Set;
        if (set == null) return new[] { name };
        var real = Sets.First(kv => ReferenceEquals(kv.Value, set)).Key;
        return new[] { real }.Concat(set.Aliases).Append(name).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>The "not available in this version" error of a set whose table is missing in this build.</summary>
    public ReadException UnavailableError(ReadUnavailable u) => new(400, "200",
        $"Not available in this version of SAP Business One (build {Build.BuildId}): " +
        (u.Reason == "key-column-missing" ? $"table {u.Table} lacks the columns {string.Join(", ", u.MissingColumns)}, so its key is not the one of the SAP Business One 2608 contract." : $"table {u.Table} is missing."));
}
