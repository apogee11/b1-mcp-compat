using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace B1.Mcp.Compat.Core.Read;

/// <summary>A typed SQL parameter. <see cref="Declaration"/> is the type as the engine declares it ("nvarchar(254)", "numeric(25,6)").</summary>
public sealed record ReadSqlParam(string Name, string Declaration, SqlDbType DbType, object Value);

/// <summary>One statement. Values of the user only travel in <see cref="Params"/>; the text holds identifiers of the package and nothing else.</summary>
public sealed record ReadSqlQuery(string Sql, IReadOnlyList<ReadSqlParam> Params)
{
    /// <summary>The parameter list as sp_executesql declares it ("@P1 nvarchar(254),@P2 int").</summary>
    public string Declarations => string.Join(",", Params.Select(p => $"{p.Name} {p.Declaration}"));
}

/// <summary>What to run for one GET: pure data, the host executes the statements and hands the rows to <see cref="ReadJson"/>.</summary>
public sealed class ReadPlan
{
    public required ReadSet Set { get; init; }
    public required string SetName { get; init; }

    /// <summary>Root select; null for Set/$count (only <see cref="Counts"/> runs).</summary>
    public ReadSqlQuery? Main { get; init; }

    /// <summary>The two COUNT statements (both are run by the engine; the count is the first one). Empty when no count was asked.</summary>
    public IReadOnlyList<ReadSqlQuery> Counts { get; init; } = Array.Empty<ReadSqlQuery>();
    public bool CountOnly { get; init; }
    public bool InlineCount { get; init; }

    /// <summary>Select list, in SQL order (the row arrays have this order).</summary>
    public IReadOnlyList<ReadColumn> Columns { get; init; } = Array.Empty<ReadColumn>();
    public bool IncludeChildren { get; init; }

    /// <summary>URL form X(key): the answer is one object, not a collection.</summary>
    public bool Single { get; init; }

    /// <summary>Answer when <see cref="Single"/> and no row came back.</summary>
    public ReadException? NotFound { get; init; }

    /// <summary>GetByKey route: the engine reads inside a SERIALIZABLE transaction (SET TRANSACTION ISOLATION LEVEL ... COMMIT, ROLLBACK when no row).</summary>
    public bool Serializable { get; init; }

    public int PageSize { get; init; }
    public int Skip { get; init; }
    public int? Top { get; init; }
    public string RawQuery { get; init; } = "";

    /// <summary>"@odata.nextLink" for a page that returned <paramref name="returnedRows"/> rows, or null (rea/18 2.6 paginacion).</summary>
    public string? NextLink(int returnedRows)
    {
        if (Single || CountOnly || Main == null || Serializable || returnedRows != PageSize) return null;
        if (Top != null && Top - PageSize <= 0) return null;
        var parts = RawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries).ToList();
        var top = parts.FindIndex(p => p.StartsWith("$top=", StringComparison.Ordinal));
        if (top >= 0) parts[top] = "$top=" + (Top!.Value - PageSize).ToString(CultureInfo.InvariantCulture);
        var skip = parts.FindIndex(p => p.StartsWith("$skip=", StringComparison.Ordinal));
        var next = "$skip=" + (Skip + PageSize).ToString(CultureInfo.InvariantCulture);
        if (skip >= 0) parts[skip] = next; else parts.Add(next);
        return SetName + "?" + string.Join("&", parts).Replace(" ", "%20");
    }

    /// <summary>Query of one child collection for one root row (N+1: one per row and per child). <paramref name="rootRow"/> has the order of <see cref="Columns"/>.</summary>
    public ReadSqlQuery ChildQuery(ReadChild child, object?[] rootRow)
    {
        if (child.Pending != null || child.Join == null || child.OrderBy == null)
            throw ReadException.NotMeasured($"child '{child.Collection}' (join/order pending in the package)");
        var bag = new ParamBag();
        var conds = new List<string>();
        foreach (var j in child.Join)
        {
            var idx = Columns.ToList().FindIndex(c => c.Name == j.ParentColumn);
            if (idx < 0) throw ReadException.NotMeasured($"child '{child.Collection}': parent column '{j.ParentColumn}' not read");
            var cc = child.Columns.First(c => c.Name == j.ChildColumn);
            conds.Add($"T0.[{cc.Name}] = ({ReadPlanner.ParentValue(cc, rootRow[idx], bag)})");
        }
        var sql = $"SELECT {string.Join(", ", child.Columns.Select(c => $"T0.[{c.Name}]"))} FROM [dbo].[{child.Table}] T0 " +
                  $"WHERE (({string.Join(" AND ", conds)} )) ORDER BY {string.Join(",", child.OrderBy.Select(o => $"T0.[{o}]"))}";
        return new(sql, bag.Items);
    }
}

internal sealed class ParamBag
{
    public List<ReadSqlParam> Items { get; } = new();

    public string Add(string declaration, SqlDbType type, object value)
    {
        var name = "@P" + (Items.Count + 1);
        Items.Add(new(name, declaration, type, value));
        return name;
    }
}

/// <summary>
/// Generic read planner: GET path + query to SQL, by the templates measured on the native Service Layer (rea/lectura-2408/recetas/plantillas.sql,
/// rea/18 2.6). No set, no build and no table is named here: the package is the only source. Only SELECT is ever built.
/// What the measurements do not cover is refused with <see cref="ReadException.NotMeasured"/>.
/// </summary>
public static partial class ReadPlanner
{
    private enum CK { Text, Int, SmallInt, Numeric, Date, Enum, Time, Blob }

    [GeneratedRegex(@"^([A-Za-z0-9_]+)(?:\((.*)\))?(/\$count)?$", RegexOptions.Singleline)]
    private static partial Regex PathRx();

    private static readonly string[] Options = { "$top", "$skip", "$select", "$orderby", "$filter", "$count", "$inlinecount", "$expand" };

    /// <param name="pathAndQuery">Relative to the service root: "Set", "Set(key)?$select=..", "Set/$count?..".</param>
    /// <param name="pageSize">Page size: Prefer odata.maxpagesize, else the Service Layer PageSize, else 20.</param>
    /// <param name="udfsOf">User fields of a table (the host reads CUFD). Their names are "U_x" and they come after the package columns.</param>
    public static ReadPlan Plan(ReadPackage pkg, string pathAndQuery, int pageSize = 20, Func<string, IReadOnlyList<ReadColumn>>? udfsOf = null)
    {
        var q = pathAndQuery.IndexOf('?');
        var path = (q < 0 ? pathAndQuery : pathAndQuery[..q]).Trim('/');
        var raw = q < 0 ? "" : pathAndQuery[(q + 1)..];
        var m = PathRx().Match(path);
        if (!m.Success) throw ReadException.NotMeasured($"path '{path}'");
        var setName = m.Groups[1].Value;
        var resolved = pkg.Resolve(setName);
        if (resolved.Unavailable != null) throw pkg.UnavailableError(resolved.Unavailable);
        var set = resolved.Set ?? throw new ReadException(400, "200", "Unrecognized resource path.");
        var hasKey = m.Groups[2].Success;
        var countPath = m.Groups[3].Success;
        if (hasKey && countPath) throw ReadException.NotMeasured("X(key)/$count");

        var all = set.Columns.ToList();
        foreach (var u in udfsOf?.Invoke(set.Table) ?? Array.Empty<ReadColumn>())
        {
            if (!u.Name.StartsWith("U_", StringComparison.Ordinal) || !ReadPackage.IsSafeIdentifier(u.Name))
                throw new InvalidDataException($"Unsafe user field name '{u.Name}'.");
            if (all.Any(c => c.Name == u.Name)) continue;   // one physical column per name
            all.Add(new ReadColumn { Name = u.Name, SqlType = u.SqlType, Nullable = true, Json = new() { Property = u.Name }, SqlTypePending = u.SqlTypePending });
        }
        if (udfsOf != null) set = WithChildUserFields(set, udfsOf);
        var opts = ParseOptions(raw);
        var env = new Env(pkg, set, all);

        // $top / $skip
        int? top = null;
        var skip = 0;
        if (opts.TryGetValue("$top", out var t))
        {
            if (!int.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var tv)) throw ReadException.NotMeasured("non numeric $top");
            if (tv < 0) throw ReadException.Query("Query string error - invalid top parameter value");
            if (tv == 0) throw new ReadException(500, "299", "Internal server error.");
            top = tv;
        }
        if (opts.TryGetValue("$skip", out var s) && (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out skip))) throw ReadException.NotMeasured("$skip that is not a non-negative integer");
        var ps = Math.Min(top ?? pageSize, pageSize);

        // $expand: the children are not navigation properties (400 with the native text)
        if (opts.TryGetValue("$expand", out var ex))
        {
            if (set.Children.Any(c => c.Collection == ex)) throw ReadException.Query($"Cannot expand invalid navigation property '{ex}' for entity type '{set.EntityType}'");
            throw ReadException.NotMeasured("$expand of anything but a child collection");
        }

        // count
        var count = false;
        if (opts.TryGetValue("$count", out var cv)) count = cv == "true" ? true : cv == "false" ? false : throw ReadException.NotMeasured($"$count={cv}");
        if (opts.TryGetValue("$inlinecount", out var ic))
            count = ic == "allpages" ? true : ic == "none" ? count : throw ReadException.NotMeasured($"$inlinecount={ic}");
        count |= countPath;

        // $select
        var selected = new List<ReadColumn>();
        var selChildren = false;
        var hasSelect = opts.TryGetValue("$select", out var sel);
        if (hasSelect)
        {
            var names = sel!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var cols = names.Where(n => set.Children.All(c => c.Collection != n)).ToList();
            selChildren = names.Length > cols.Count;
            if (selChildren && cols.Count > 0) throw ReadException.NotMeasured("$select that mixes properties and a child collection");
            foreach (var n in cols) selected.Add(env.Exposed(n));
            if (selChildren) selected = set.PrimaryKey.Select(k => env.Column(k)).ToList();
        }
        var cols0 = hasSelect ? selected : all;
        // reading every column needs no type (the SQL names the columns only); a pending type is refused when it is asked for or when the JSON is written
        if (hasSelect) foreach (var c in selected) env.Touch(c);

        // key in the URL
        var bag = new ParamBag();
        string? where = null;
        if (hasKey)
        {
            if (opts.ContainsKey("$filter") || opts.ContainsKey("$orderby") || count) throw ReadException.NotMeasured("X(key) with $filter, $orderby or a count");
            var parts = KeyParts(env, m.Groups[2].Value, bag, hasSelect);
            where = "WHERE " + string.Join(" AND ", parts);
        }
        else if (opts.TryGetValue("$filter", out var f))
        {
            var node = ReadFilterParser.Parse(f);
            var keyEq = hasSelect || opts.ContainsKey("$orderby") || count ? null : KeyEqualities(env, node);
            if (keyEq != null)
            {
                // an equality on the whole primary key takes the GetByKey route (no paging, $top/$skip ignored)
                var parts = set.PrimaryKey.Select(k => $"T0.[{k}] = ({Typed(env, env.Column(k), keyEq[k], bag)})").ToList();
                var sql1 = $"SELECT {SelectList(all, true)} FROM [dbo].[{set.Table}] T0 WHERE {string.Join(" AND ", parts)} ORDER BY {string.Join(",", set.PrimaryKey.Select(k => $"T0.[{k}]"))}";
                return new ReadPlan
                {
                    Set = set, SetName = setName, Main = new(sql1, bag.Items), Columns = all, IncludeChildren = true, Serializable = true,
                    PageSize = ps, Skip = 0, Top = top, RawQuery = raw,
                };
            }
            where = "WHERE " + Render(env, node, bag, true);
        }

        // $orderby
        var order = "CURRENT_TIMESTAMP";
        if (opts.TryGetValue("$orderby", out var ob))
        {
            var items = new List<string>();
            foreach (var item in ob.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var w = item.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (w.Length is < 1 or > 2 || (w.Length == 2 && w[1] is not ("asc" or "desc"))) throw ReadException.Query("Query string error - Invalid symbol in the orderby condition");
                var c = env.Exposed(w[0]);
                env.Touch(c);
                if (CKind(env, c) == CK.Blob) throw ReadException.NotMeasured($"$orderby on '{w[0]}'");
                items.Add($"T0.[{c.Name}]" + (w.Length == 2 && w[1] == "desc" ? " DESC" : ""));
            }
            order = string.Join(",", items);
        }

        var whereSql = where == null ? "" : where + " ";
        var counts = !count ? new List<ReadSqlQuery>() : new List<ReadSqlQuery>
        {
            new($"SELECT COUNT('A') FROM [dbo].[{set.Table}] T0 {whereSql}".TrimEnd(), bag.Items),
            new($"SELECT COUNT(T0.[{set.PrimaryKey[0]}]) FROM [dbo].[{set.Table}] T0 {whereSql}".TrimEnd(), bag.Items),
        };
        var includeChildren = !hasSelect || selChildren;
        if (countPath)
            return new ReadPlan { Set = set, SetName = setName, Counts = counts, CountOnly = true, PageSize = ps, Skip = skip, Top = top, RawQuery = raw };

        var listCols = SelectList(cols0, !hasSelect);
        // GetByKey: no $select, no paging
        if (hasKey && !hasSelect)
        {
            var orderKey = string.Join(",", set.PrimaryKey.Select(k => $"T0.[{k}]"));
            return new ReadPlan
            {
                Set = set, SetName = setName, Main = new($"SELECT {listCols} FROM [dbo].[{set.Table}] T0 {whereSql}ORDER BY {orderKey}", bag.Items),
                Columns = cols0, IncludeChildren = true, Single = true, Serializable = true, PageSize = ps, Top = top, RawQuery = raw,
                NotFound = new ReadException(404, "-2028", "No matching records found (ODBC -2028)"),
            };
        }
        var pageN = hasKey ? pageSize : ps;
        var pageSkip = hasKey ? 0 : skip;
        return new ReadPlan
        {
            Set = set, SetName = setName,
            Main = new($"SELECT {listCols} FROM [dbo].[{set.Table}] T0 {whereSql}ORDER BY {order} OFFSET {pageSkip} ROWS FETCH NEXT {pageN} ROWS ONLY", bag.Items),
            Counts = counts, InlineCount = count, Columns = cols0, IncludeChildren = includeChildren, Single = hasKey,
            NotFound = hasKey ? new ReadException(404, "-2028", "Entity with the specified value does not exist or the navigation property has no content.") : null,
            PageSize = pageN, Skip = pageSkip, Top = top, RawQuery = raw,
        };
    }

    private static string SelectList(IEnumerable<ReadColumn> cols, bool all) =>
        string.Join(all ? " , " : ", ", cols.Select(c => $"T0.[{c.Name}]"));

    private static Dictionary<string, string> ParseOptions(string raw)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var seg in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = seg.IndexOf('=');
            var k = Uri.UnescapeDataString(i < 0 ? seg : seg[..i]);
            if (!k.StartsWith('$')) continue;   // not an OData option (custom parameter): not ours
            if (!Options.Contains(k)) throw ReadException.NotMeasured($"option {k}");
            d[k] = i < 0 ? "" : Uri.UnescapeDataString(seg[(i + 1)..]);
        }
        return d;
    }

    // ---- columns and literals -------------------------------------------------------------------------------------

    private sealed class Env
    {
        public ReadPackage Pkg { get; }
        public ReadSet Set { get; }
        private readonly List<ReadColumn> _cols;

        public Env(ReadPackage pkg, ReadSet set, List<ReadColumn> cols) { Pkg = pkg; Set = set; _cols = cols; }

        public ReadColumn Column(string name) => _cols.First(c => c.Name == name);

        /// <summary>Column of a JSON property; one the entity does not expose is the native -1000 error.</summary>
        public ReadColumn Exposed(string property) =>
            _cols.FirstOrDefault(c => c.Json?.Property == property)
            ?? throw new ReadException(400, "-1000", $"Property '{property}' of '{Set.EntityType}'is invalid");

        public void Touch(ReadColumn c)
        {
            if (c.SqlTypePending != null) throw ReadException.NotMeasured($"column '{c.Name}' (SQL type pending in the package: {c.SqlTypePending})");
        }
    }

    /// <summary>Copy of the set whose children also read their user fields (the engine lists every column of the child table, UDF included).</summary>
    private static ReadSet WithChildUserFields(ReadSet set, Func<string, IReadOnlyList<ReadColumn>> udfsOf)
    {
        if (set.Children.Count == 0) return set;
        var kids = new List<ReadChild>();
        foreach (var ch in set.Children)
        {
            var extra = udfsOf(ch.Table).Where(u => ch.Columns.All(c => c.Name != u.Name)).ToList();
            if (extra.Count == 0) { kids.Add(ch); continue; }
            foreach (var u in extra)
                if (!u.Name.StartsWith("U_", StringComparison.Ordinal) || !ReadPackage.IsSafeIdentifier(u.Name)) throw new InvalidDataException($"Unsafe user field name '{u.Name}'.");
            kids.Add(new ReadChild
            {
                Collection = ch.Collection, EntityType = ch.EntityType, Table = ch.Table, Join = ch.Join, OrderBy = ch.OrderBy, Pending = ch.Pending,
                Columns = ch.Columns.Concat(extra.Select(u => new ReadColumn { Name = u.Name, SqlType = u.SqlType, Nullable = true, Json = new() { Property = u.Name }, SqlTypePending = u.SqlTypePending })).ToList(),
                PropertyOrder = ch.PropertyOrder?.Concat(extra.Select(u => u.Name)).ToList(),
            });
        }
        return new ReadSet
        {
            EntityType = set.EntityType, Label = set.Label, Table = set.Table, PrimaryKey = set.PrimaryKey, Aliases = set.Aliases, Columns = set.Columns,
            OmittedColumns = set.OmittedColumns, Children = kids, Rules = set.Rules, PropertyOrder = set.PropertyOrder,
        };
    }

    private static CK CKind(Env env, ReadColumn c)
    {
        if (c.SqlTypePending != null) return CK.Blob;
        if (c.Json?.Enum != null && env.Pkg.Enums.ContainsKey(c.Json.Enum)) return CK.Enum;
        if (c.Json?.EdmType == "Edm.TimeOfDay") return CK.Time;
        return c.BaseType switch
        {
            "nvarchar" or "char" or "varchar" => CK.Text,
            "int" => CK.Int,
            "smallint" => CK.SmallInt,
            "numeric" or "decimal" => CK.Numeric,
            "datetime" => CK.Date,
            _ => CK.Blob,
        };
    }

    /// <summary>Typed parameter for a literal against a column (rea plantillas L2); returns the parameter name.</summary>
    private static string Typed(Env env, ReadColumn c, ReadLit lit, ParamBag bag)
    {
        var inv = CultureInfo.InvariantCulture;
        switch (CKind(env, c))
        {
            case CK.Text when lit.Kind == ReadLitKind.Str:
                return bag.Add("nvarchar(254)", SqlDbType.NVarChar, lit.Text);
            case CK.Int when lit.Kind == ReadLitKind.Int && int.TryParse(lit.Text, NumberStyles.AllowLeadingSign, inv, out var i):
                return bag.Add("int", SqlDbType.Int, i);
            case CK.SmallInt when lit.Kind == ReadLitKind.Int && short.TryParse(lit.Text, NumberStyles.AllowLeadingSign, inv, out var sh):
                return bag.Add("smallint", SqlDbType.SmallInt, sh);
            case CK.Numeric when lit.Kind is ReadLitKind.Int or ReadLitKind.Dec:
                return bag.Add("numeric(25,6)", SqlDbType.Decimal, decimal.Parse(lit.Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, inv));
            case CK.Date when lit.Kind == ReadLitKind.Date:
            {
                var dt = DateTime.Parse(lit.Text, inv, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
                if (dt.TimeOfDay != TimeSpan.Zero) throw ReadException.NotMeasured("a date literal with a time other than 00:00:00 (datetime-nonzero-time)");
                return bag.Add("datetime2", SqlDbType.DateTime2, DateTime.SpecifyKind(dt, DateTimeKind.Unspecified));
            }
            case CK.Enum when lit.Kind == ReadLitKind.Str && c.BaseType is "char" or "nvarchar":
            {
                var map = env.Pkg.Enums[c.Json!.Enum!];
                var db = map.FirstOrDefault(kv => kv.Value == lit.Text);
                if (db.Key == null)
                    throw new ReadException(400, "-1013", $"Invalid item name '{lit.Text}' in Enum '{c.Json.Enum}'. The valid names are: " +
                        string.Join(", ", map.Select(kv => $"'{kv.Value}'-'{kv.Key}'")));
                return bag.Add(c.SqlType, c.BaseType == "char" ? SqlDbType.Char : SqlDbType.NVarChar, db.Key);
            }
            default:
                throw ReadException.NotMeasured($"a {lit.Kind} literal against column '{c.Name}' ({c.SqlType}{(c.Json?.Enum != null ? ", enum" : "")})");
        }
    }

    /// <summary>Value of the parent key as a child join parameter: "converted to the type of the child column" (NFN4/NFN5: '--' becomes 0).</summary>
    internal static string ParentValue(ReadColumn child, object? v, ParamBag bag)
    {
        var inv = CultureInfo.InvariantCulture;
        if (v == null) throw ReadException.NotMeasured("a child join on a NULL parent key");
        switch (child.BaseType)
        {
            case "int": return bag.Add("int", SqlDbType.Int, v is string s1 ? (int.TryParse(s1, NumberStyles.AllowLeadingSign, inv, out var a) ? a : 0) : Convert.ToInt32(v, inv));
            case "smallint": return bag.Add("smallint", SqlDbType.SmallInt, v is string s2 ? (short.TryParse(s2, NumberStyles.AllowLeadingSign, inv, out var b) ? b : (short)0) : Convert.ToInt16(v, inv));
            case "nvarchar" or "char": return bag.Add("nvarchar(254)", SqlDbType.NVarChar, Convert.ToString(v, inv)!);
            default: throw ReadException.NotMeasured($"a child join on column '{child.Name}' ({child.SqlType})");
        }
    }

    // ---- $filter to SQL --------------------------------------------------------------------------------------------

    private static string Render(Env env, ReadFilterNode n, ParamBag bag, bool top)
    {
        switch (n)
        {
            case ReadCmp c:
            {
                var col = env.Exposed(c.Property);
                env.Touch(col);
                if (c.Value.Kind == ReadLitKind.Null)
                {
                    // measured on the native 2608 (SQL by Extended Events): eq null is IS NULL, ne null is IS NOT NULL; lt/le/gt/ge is a 400
                    if (c.Op is not ("eq" or "ne")) throw ReadException.Query("Query string error - not allowed operation on 'null'");
                    return c.Op == "eq" ? $"T0.[{col.Name}] IS NULL" : $"T0.[{col.Name}] IS NOT NULL";
                }
                if (CKind(env, col) == CK.Enum && c.Op is not ("eq" or "ne")) throw ReadException.NotMeasured($"'{c.Op}' on enum '{c.Property}'");
                var sym = c.Op switch { "eq" => "=", "ne" => "<>", "lt" => "<", "le" => "<=", "gt" => ">", _ => ">=" };
                return $"T0.[{col.Name}] {sym} ({Typed(env, col, c.Value, bag)})";
            }
            case ReadLikeFn l:
            {
                var col = env.Exposed(l.Property);
                env.Touch(col);
                if (CKind(env, col) != CK.Text) throw ReadException.NotMeasured($"{l.Mode} on non-text column '{l.Property}'");
                var text = l.Mode switch { ReadLike.Contains => $"%{l.Text}%", ReadLike.StartsWith => $"{l.Text}%", _ => $"%{l.Text}" };
                // the engine declares nvarchar(256 + number of '%'): a driver artefact that is reproduced
                var p = bag.Add($"nvarchar({256 + text.Count(ch => ch == '%')})", SqlDbType.NVarChar, text);
                return $"T0.[{col.Name}] Like {p}";
            }
            case ReadNot nt:
                return $"NOT ({Render(env, nt.Inner, bag, false)} )";
            case ReadLogic g:
            {
                var parts = g.Items.Select(i => i is ReadLogic gi && gi.Op != g.Op ? $"({Render(env, i, bag, false)} )" : Render(env, i, bag, false));
                return string.Join($" {g.Op} ", parts);
            }
            default: throw ReadFilterParser.Invalid();
        }
    }

    /// <summary>When the filter is only equalities (and) on every primary key column: property to literal by column name. Otherwise null.</summary>
    private static Dictionary<string, ReadLit>? KeyEqualities(Env env, ReadFilterNode n)
    {
        var items = n is ReadLogic { Op: "AND" } g ? g.Items : new List<ReadFilterNode> { n };
        var d = new Dictionary<string, ReadLit>();
        foreach (var i in items)
        {
            if (i is not ReadCmp { Op: "eq" } c || c.Value.Kind == ReadLitKind.Null) return null;
            var col = env.Set.Columns.FirstOrDefault(x => x.Json?.Property == c.Property);
            if (col == null || !env.Set.PrimaryKey.Contains(col.Name) || !d.TryAdd(col.Name, c.Value)) return null;
        }
        return d.Count == env.Set.PrimaryKey.Count ? d : null;
    }

    // ---- key in the URL ---------------------------------------------------------------------------------------------

    private static List<string> KeyParts(Env env, string text, ParamBag bag, bool hasSelect)
    {
        var pk = env.Set.PrimaryKey;
        var items = SplitTop(text);
        var byCol = new Dictionary<string, string>();
        if (items.Count > 0 && items.All(i => NamedKey(i) != null))
        {
            foreach (var i in items)
            {
                var (name, val) = NamedKey(i)!.Value;
                var col = env.Set.Columns.FirstOrDefault(c => c.Json?.Property == name && pk.Contains(c.Name)) ?? throw ReadException.NotMeasured($"key part '{name}'");
                if (!byCol.TryAdd(col.Name, val)) throw ReadException.NotMeasured("a repeated key part");
            }
        }
        else if (items.Count == pk.Count && items.All(i => NamedKey(i) == null))
        {
            for (var i = 0; i < pk.Count; i++) byCol[pk[i]] = items[i];
        }
        else if (pk.Count == 1 && items.Count == 1) byCol[pk[0]] = items[0];
        if (byCol.Count != pk.Count) throw ReadException.NotMeasured("a key that does not name every primary key column");

        // rule: composite key with an enum and $select is a 400 in the native engine
        if (hasSelect && pk.Count > 1 && env.Set.HasRule("clave-compuesta-enum-select-400") && pk.Any(k => CKind(env, env.Column(k)) == CK.Enum))
            throw ReadException.Query("Invalid query option: key value is not matched with its type");

        var parts = new List<string>();
        foreach (var k in pk)
        {
            var col = env.Column(k);
            var lit = ReadFilterParser.ParseLiteral(byCol[k]);
            var kind = CKind(env, col);
            if (kind == CK.Date) throw ReadException.NotMeasured("a key with a date (clave-con-fecha)");
            if (kind is CK.Int or CK.SmallInt or CK.Numeric && lit.Kind == ReadLitKind.Str)
                throw new ReadException(400, "203", "Bad Request - Error in query syntax.");
            if (kind == CK.SmallInt && lit.Kind == ReadLitKind.Int && env.Set.HasRule("smallint-clave-trunca")
                && !short.TryParse(lit.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
                lit = new(ReadLitKind.Int, "0");   // rule: an out of range smallint key is read as 0
            parts.Add($"T0.[{k}] = ({Typed(env, col, lit, bag)})");
        }
        return parts;
    }

    private static (string, string)? NamedKey(string item)
    {
        if (item.StartsWith('\'')) return null;
        var i = item.IndexOf('=');
        return i > 0 ? (item[..i].Trim(), item[(i + 1)..].Trim()) : null;
    }

    private static List<string> SplitTop(string s)
    {
        var l = new List<string>();
        var sb = new StringBuilder();
        var q = false;
        foreach (var ch in s)
        {
            if (ch == '\'') q = !q;
            if (ch == ',' && !q) { l.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(ch);
        }
        if (sb.Length > 0 || l.Count > 0) l.Add(sb.ToString().Trim());
        return l;
    }
}
