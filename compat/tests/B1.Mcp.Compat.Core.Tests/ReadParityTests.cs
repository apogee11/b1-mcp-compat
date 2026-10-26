using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using B1.Mcp.Compat.Core.Read;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>
/// SQL of the generic planner against the SQL the native 2608 Service Layer ran (Extended Events capture,
/// rea/lectura-2408/recetas/sql_capturado_limpio.jsonl) with the real 2408 SP package. Skipped silently when the repo files are not there.
/// </summary>
public class ReadParityTests
{
    private static readonly Regex Exec = new(@"^exec sp_executesql N'((?:[^']|'')*)'(?:,N'((?:[^']|'')*)'(?:,(.*))?)?$", RegexOptions.Singleline);

    private static (string Sql, string Decl, List<string> Values) Parse(string stmt)
    {
        var m = Exec.Match(stmt.Trim());
        if (!m.Success) return (stmt, "", new());
        var vals = new List<string>();
        var sb = new StringBuilder();
        var q = false;
        foreach (var ch in m.Groups[3].Value)
        {
            if (ch == '\'') q = !q;
            if (ch == ',' && !q) { vals.Add(Clean(sb.ToString())); sb.Clear(); } else sb.Append(ch);
        }
        if (sb.Length > 0) vals.Add(Clean(sb.ToString()));
        return (m.Groups[1].Value.Replace("''", "'"), m.Groups[2].Value, vals);
    }

    private static string Clean(string v)
    {
        v = v.Trim();
        if (v.StartsWith("N'")) v = v[1..];
        return v.StartsWith('\'') ? v[1..^1].Replace("''", "'") : v;
    }

    private static string Value(object v) => v switch
    {
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.000000", CultureInfo.InvariantCulture),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture)!,
    };

    private static string NormDecl(string d) => Regex.Replace(d, @"\s+", "");

    private static (ReadPackage, List<(string Set, string Case, string Path, List<string> Sql)>)? Load()
    {
        var repo = ReadTestData.Repo();
        var pkgFile = repo == null ? null : Path.Combine(repo, "rea", "kbgen", "out", "read-B11000SP_2408.json.gz");
        var capFile = repo == null ? null : Path.Combine(repo, "rea", "lectura-2408", "recetas", "sql_capturado_limpio.jsonl");
        if (pkgFile == null || !File.Exists(pkgFile) || !File.Exists(capFile!)) return null;
        using var f = File.OpenRead(pkgFile);
        var pkg = ReadPackage.LoadGz(f);
        var rows = new List<(string, string, string, List<string>)>();
        // the capture has a byte that is not UTF-8: decode tolerantly
        foreach (var line in Encoding.UTF8.GetString(File.ReadAllBytes(capFile!)).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var d = JsonDocument.Parse(line);
            var r = d.RootElement;
            rows.Add((r.GetProperty("set").GetString()!, r.GetProperty("case").GetString()!, r.GetProperty("path").GetString()!,
                r.GetProperty("sql_relevante").EnumerateArray().Select(x => x.GetString()!).ToList()));
        }
        return (pkg, rows);
    }

    // sets whose 2608 capture and 2408 package differ by design (columns that 2408 lacks, user fields of the demo company)
    private static bool Skippable(ReadPackage p, string set)
    {
        var s = p.Resolve(set).Set;
        return s == null || s.OmittedColumns.Count > 0 || set is "Items" or "Warehouses";
    }

    // the 2608 table has trailing columns the 2408 table lacks: the 2408 package is a prefix of the captured select list and the rest of the statement is equal
    private static bool NewerTableColumns(string got, string exp)
    {
        var (g, e) = (ReadTestData.Norm(got), ReadTestData.Norm(exp));
        var (gi, ei) = (g.IndexOf(" FROM "), e.IndexOf(" FROM "));
        return g[(gi)..] == e[(ei)..] && e[..ei].StartsWith(g[..gi] + ", ") ;
    }

    private static List<string> Selects(List<string> sql, string table, bool count) =>
        sql.Where(x => x.Contains($"[dbo].[{table}] T0") && (count ? x.Contains("COUNT(") : x.Contains("SELECT T0.[")) && !x.TrimStart().StartsWith("--")).ToList();

    private static readonly string[] ListCases =
    {
        "top1", "top3_skip1", "all_default", "orderby", "filter_eq", "filter_contains", "select", "filter_select_order_skip", "key_select", "extra0", "extra1", "extra2",
        "startswith", "endswith", "ne", "lt_int", "null_eq", "not", "and_or", "orderby_multi", "date_filter_ge_date", "dec_eq", "dec_select", "enum_select", "enum_orderby",
        "yesno_filter_no", "skip_only", "keyfilter_plus", "key", "keyfilter_simple", "page_exact3", "wh_udf_key", "ors_key",
    };

    [Fact]
    public void ListSelectOrderbyFilterAndKeySqlEqualsTheNativeCapture()
    {
        var l = Load();
        if (l == null) return;
        var (pkg, rows) = l.Value;
        int compared = 0, newerTable = 0;
        var diffs = new List<string>();
        foreach (var (set, kase, rawPath, sql) in rows)
        {
            var path = rawPath.Replace("/b1s/v2/", "");
            if (!ListCases.Contains(kase) || Skippable(pkg, set)) continue;
            var table = pkg.Resolve(set).Set!.Table;
            var exp = Selects(sql, table, false).Select(Parse).FirstOrDefault();
            if (exp.Sql == null) continue;
            var pageSize = kase == "page_exact3" ? 3 : 20;
            ReadPlan plan;
            try { plan = ReadPlanner.Plan(pkg, path, pageSize); }
            catch (ReadException e) { diffs.Add($"{set} {kase}: {e.Message}"); continue; }
            compared++;
            var got = plan.Main!;
            if (ReadTestData.Norm(got.Sql) != ReadTestData.Norm(exp.Sql) && NewerTableColumns(got.Sql, exp.Sql)) { newerTable++; continue; }
            else if (ReadTestData.Norm(got.Sql) != ReadTestData.Norm(exp.Sql)) diffs.Add($"{set} {kase}: SQL\n  {ReadTestData.Norm(got.Sql)}\n  {ReadTestData.Norm(exp.Sql)}");
            else if (NormDecl(got.Declarations) != NormDecl(exp.Decl)) diffs.Add($"{set} {kase}: params {got.Declarations} vs {exp.Decl}");
            else if (!got.Params.Select(p => Value(p.Value)).SequenceEqual(exp.Values.Select(v => decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var dv) && v.Contains('.') ? dv.ToString("0.000000", CultureInfo.InvariantCulture) : v)))
                diffs.Add($"{set} {kase}: values {string.Join("|", got.Params.Select(p => Value(p.Value)))} vs {string.Join("|", exp.Values)}");
        }
        Assert.True(diffs.Count == 0, $"{diffs.Count} differences, {compared} compared:\n" + string.Join("\n", diffs.Take(12)));
        Assert.True(newerTable <= 6, $"{newerTable} sets whose 2608 table has more columns than the 2408 package");
        Assert.True(compared >= 150, $"only {compared} captured statements compared");
    }

    [Fact]
    public void CountSqlEqualsTheNativeCapture()
    {
        var l = Load();
        if (l == null) return;
        var (pkg, rows) = l.Value;
        var n = 0;
        foreach (var (set, kase, rawPath, sql) in rows)
        {
            if (kase is not ("count_inline" or "count_path" or "count_filter" or "inlinecount")) continue;
            var table = pkg.Resolve(set).Set!.Table;
            var exp = Selects(sql, table, true).Select(Parse).ToList();
            var plan = ReadPlanner.Plan(pkg, rawPath.Replace("/b1s/v2/", ""));
            Assert.Equal(2, plan.Counts.Count);
            for (var i = 0; i < 2; i++)
            {
                Assert.Equal(ReadTestData.Norm(exp[i].Sql), ReadTestData.Norm(plan.Counts[i].Sql));
                Assert.Equal(NormDecl(exp[i].Decl), NormDecl(plan.Counts[i].Declarations));
            }
            n++;
        }
        Assert.True(n >= 4);
    }

    [Fact]
    public void EnumErrorMatchesTheNativeText()
    {
        var l = Load();
        if (l == null) return;
        var (pkg, _) = l.Value;
        var e = Assert.Throws<ReadException>(() => ReadPlanner.Plan(pkg, "PaymentWizards?$filter=OutgoingType eq 'zzz'&$top=2"));
        Assert.Equal("Invalid item name 'zzz' in Enum 'PaymentWizardTypeEnum'. The valid names are: 'pwt_Outgoing'-'O', 'pwt_Incoming'-'I', 'pwt_Both'-'B'", e.Message);
    }

    [Fact]
    public void NativeErrorTextsAreReproduced()
    {
        var l = Load();
        if (l == null) return;
        var (pkg, _) = l.Value;
        Assert.Equal("Query string error - Not supported function", Assert.Throws<ReadException>(() => ReadPlanner.Plan(pkg, "HelpLinks?$filter=tolower(Provider) eq 'sap'&$top=2")).Message);
        Assert.Equal("Query string error - Not supported function", Assert.Throws<ReadException>(() => ReadPlanner.Plan(pkg, "HelpLinks?$filter=AbsEntry in (-99,-98)&$top=3")).Message);
        Assert.Equal("Property 'Nope' of 'HelpLink'is invalid", Assert.Throws<ReadException>(() => ReadPlanner.Plan(pkg, "HelpLinks?$select=Nope")).Message);
        Assert.Equal("Query string error - invalid top parameter value", Assert.Throws<ReadException>(() => ReadPlanner.Plan(pkg, "HelpLinks?$top=-1")).Message);
        Assert.Equal("Bad Request - Error in query syntax.", Assert.Throws<ReadException>(() => ReadPlanner.Plan(pkg, "HelpLinks('abc')")).Message);
        Assert.Equal("Unrecognized resource path.", Assert.Throws<ReadException>(() => ReadPlanner.Plan(pkg, "NoSuchSet?$top=1")).Message);
    }

    [Fact]
    public void EveryPackageSetPlansItsTopOneListWithoutError()
    {
        var l = Load();
        if (l == null) return;
        var (pkg, _) = l.Value;
        foreach (var name in pkg.Sets.Keys)
        {
            var plan = ReadPlanner.Plan(pkg, name + "?$top=1");
            Assert.StartsWith("SELECT T0.[", plan.Main!.Sql);
            Assert.EndsWith("FETCH NEXT 1 ROWS ONLY", plan.Main.Sql.Trim());
        }
    }

    // user fields of the 2608 demo (CUFD rows, TypeID/EditType/SizeID as stored)
    private static readonly UserFieldRow[] DemoUdf =
    {
        new("OITM", 0, "SBOCTXM", "M", null, 8), new("OITM", 1, "MAPI", "A", null, 10), new("OITM", 2, "SBOMAPI", "A", null, 10),
        new("OWHS", 0, "MAPW", "A", null, 10), new("OWHS", 1, "SBOMAPW", "A", null, 10),
    };

    /// <summary>Items and Warehouses are not in the 2408 package (the Service Layer exposes them): a package with the columns of the captured statement minus the user fields stands in for them.</summary>
    private static ReadPackage CapturedPackage(List<(string Set, string Case, string Path, List<string> Sql)> rows)
    {
        string Cols(string kase, string table) =>
            string.Join(",", Regex.Matches(Selects(rows.First(r => r.Case == kase).Sql, table, false).First(), @"T0\.\[(\w+)\]").Select(m => m.Groups[1].Value)
                .Where(c => !c.StartsWith("U_")).Select(c => $"{{\"name\":\"{c}\",\"sqlType\":\"nvarchar(50)\",\"nullable\":true,\"json\":{{\"property\":\"{(c == "WhsCode" ? "WarehouseCode" : c)}\"}}}}"));
        var json = "{\"schemaVersion\":1,\"build\":{\"package\":\"T\",\"buildId\":\"1.0.0.0-aaaaaaaaaaaa\",\"loginVersion\":\"1\"},\"enums\":{},\"sets\":{"
            + "\"Items\":{\"entityType\":\"Item\",\"table\":\"OITM\",\"primaryKey\":[\"ItemCode\"],\"aliases\":[],\"columns\":[" + Cols("items_all_top1", "OITM") + "],\"children\":[],\"rules\":[]},"
            + "\"Warehouses\":{\"entityType\":\"Warehouse\",\"table\":\"OWHS\",\"primaryKey\":[\"WhsCode\"],\"aliases\":[],\"columns\":[" + Cols("wh_udf_all", "OWHS") + "],\"children\":[],\"rules\":[]}},"
            + "\"unavailable\":{}}";
        return ReadPackage.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void UserFieldSqlEqualsTheNativeCapture()
    {
        var l = Load();
        if (l == null) return;
        var (_, rows) = l.Value;
        var pkg = CapturedPackage(rows);
        var n = 0;
        foreach (var (set, kase, rawPath, sql) in rows)
        {
            if (kase is not ("items_udf_select" or "items_udf_filter" or "items_udf_order" or "wh_udf_select" or "items_all_top1" or "wh_udf_all")) continue;
            var table = pkg.Resolve(set).Set!.Table;
            var exp = Selects(sql, table, false).Select(Parse).First();
            var plan = ReadPlanner.Plan(pkg, rawPath.Replace("/b1s/v2/", ""), 20, t => UserFields.ColumnsOf(DemoUdf, t));
            Assert.Equal(ReadTestData.Norm(exp.Sql), ReadTestData.Norm(plan.Main!.Sql));
            Assert.Equal(NormDecl(exp.Decl), NormDecl(plan.Main!.Declarations));
            n++;
        }
        Assert.Equal(6, n);
    }
}
