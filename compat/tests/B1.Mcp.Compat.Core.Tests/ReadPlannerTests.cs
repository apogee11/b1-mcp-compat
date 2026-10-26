using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using B1.Mcp.Compat.Core.Read;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>Synthetic package (no set or build of the product): exercises every rule of the generic read runtime.</summary>
internal static class ReadTestData
{
    public const string PackageJson = """
    {
      "schemaVersion": 1,
      "build": { "package": "TEST_1", "buildId": "1.0.0.0-aaaaaaaaaaaa", "loginVersion": "1000001" },
      "enums": { "KindEnum": { "A": "k_A", "B": "k_B" } },
      "sets": {
        "Things": {
          "entityType": "Thing", "table": "OTHG", "primaryKey": ["Code"], "aliases": ["Thingies"],
          "columns": [
            { "name": "Code", "sqlType": "nvarchar(20)", "nullable": false, "json": { "property": "Code", "edmType": "Edm.String" } },
            { "name": "Descr", "sqlType": "nvarchar(100)", "nullable": true, "json": { "property": "Name", "edmType": "Edm.String" } },
            { "name": "KindCol", "sqlType": "char(1)", "nullable": true, "json": { "property": "Kind", "edmType": "SAPB1.KindEnum", "enum": "KindEnum" } },
            { "name": "Qty", "sqlType": "int", "nullable": true, "json": { "property": "Quantity", "edmType": "Edm.Int32" } },
            { "name": "Small", "sqlType": "smallint", "nullable": true, "json": { "property": "Small", "edmType": "Edm.Int32" } },
            { "name": "DocTime", "sqlType": "smallint", "nullable": true, "json": { "property": "DocTime", "edmType": "Edm.TimeOfDay" } },
            { "name": "Price", "sqlType": "numeric(19,6)", "nullable": true, "json": { "property": "Price", "edmType": "Edm.Double" } },
            { "name": "Created", "sqlType": "datetime", "nullable": true, "json": { "property": "CreateDate", "edmType": "Edm.DateTimeOffset" } },
            { "name": "Memo", "sqlType": "ntext", "nullable": true, "sqlTypePending": "sqltype-M", "json": null },
            { "name": "Internal", "sqlType": "nvarchar(10)", "nullable": true, "json": null }
          ],
          "omittedColumns": [],
          "propertyOrder": ["Code", "Name", "Kind", "Quantity", "Small", "DocTime", "Price", "CreateDate"],
          "children": [
            { "collection": "ThingLines", "entityType": "ThingLine", "table": "THG1",
              "join": [ { "childColumn": "Code", "parentColumn": "Code" } ], "orderBy": ["Code", "LineNum"],
              "propertyOrder": ["Code", "LineNum", "Text"],
              "columns": [
                { "name": "Code", "sqlType": "nvarchar(20)", "nullable": false, "json": { "property": "Code" } },
                { "name": "LineNum", "sqlType": "int", "nullable": false, "json": { "property": "LineNum" } },
                { "name": "Text", "sqlType": "nvarchar(10)", "nullable": true, "json": { "property": "Text" } } ] },
            { "collection": "ThingPending", "entityType": "ThingPending", "table": "THG2", "join": null, "orderBy": null,
              "pending": { "id": "child-order-multikey" }, "columns": [ { "name": "Code", "sqlType": "nvarchar(20)", "nullable": false, "json": { "property": "Code" } } ] }
          ],
          "rules": []
        },
        "Blobs": {
          "entityType": "Blob", "table": "OBLB", "primaryKey": ["Id"], "aliases": [],
          "columns": [
            { "name": "Id", "sqlType": "int", "nullable": false, "json": { "property": "Id" } },
            { "name": "Memo", "sqlType": "ntext", "nullable": true, "sqlTypePending": "sqltype-M", "json": { "property": "Memo" } }
          ],
          "propertyOrder": ["Id", "Memo"], "children": [], "rules": []
        },
        "Pairs": {
          "entityType": "Pair", "table": "OPAI", "primaryKey": ["A", "B"], "aliases": [],
          "columns": [
            { "name": "A", "sqlType": "int", "nullable": false, "json": { "property": "A" } },
            { "name": "B", "sqlType": "char(1)", "nullable": false, "json": { "property": "B", "edmType": "SAPB1.KindEnum", "enum": "KindEnum" } },
            { "name": "S", "sqlType": "smallint", "nullable": false, "json": { "property": "S" } }
          ],
          "children": [], "rules": [ { "name": "clave-compuesta-enum-select-400" } ]
        }
      },
      "unavailable": { "Gone": { "table": "OGON", "missingTables": ["OGON"], "reason": "table-missing" } }
    }
    """;

    public static ReadPackage Pkg() => ReadPackage.Load(new MemoryStream(Encoding.UTF8.GetBytes(PackageJson)));

    public static string Norm(string s) => Regex.Replace(Regex.Replace(s, @"\s+", " "), @" ,", ",").Trim();

    /// <summary>Folder that holds rea/ (the repo root), or null when the tests run without the repo.</summary>
    public static string? Repo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "rea", "lectura-2408"))) return d.FullName;
        return null;
    }
}

public class ReadPackageTests
{
    [Fact]
    public void RejectsUnknownSchemaVersion()
    {
        var e = Assert.Throws<InvalidDataException>(() => ReadPackage.Load(new MemoryStream(Encoding.UTF8.GetBytes("""{"schemaVersion":2}"""))));
        Assert.Contains("schemaVersion 2", e.Message);
        Assert.Throws<InvalidDataException>(() => ReadPackage.Load(new MemoryStream(Encoding.UTF8.GetBytes("{}"))));
    }

    [Fact]
    public void RejectsUnsafeIdentifiersAndUnknownRules()
    {
        var bad = ReadTestData.PackageJson.Replace("\"OTHG\"", "\"OTHG] T0; DROP TABLE X;--\"");
        Assert.Throws<InvalidDataException>(() => ReadPackage.Load(new MemoryStream(Encoding.UTF8.GetBytes(bad))));
        var rule = ReadTestData.PackageJson.Replace("\"rules\": []", "\"rules\": [ { \"name\": \"made-up\" } ]");
        Assert.Contains("made-up", Assert.Throws<InvalidDataException>(() => ReadPackage.Load(new MemoryStream(Encoding.UTF8.GetBytes(rule)))).Message);
    }

    [Fact]
    public void LoadsGzAndResolvesAliases()
    {
        var ms = new MemoryStream();
        using (var z = new GZipStream(ms, CompressionLevel.Fastest, true)) z.Write(Encoding.UTF8.GetBytes(ReadTestData.PackageJson));
        ms.Position = 0;
        var p = ReadPackage.LoadGz(ms);
        Assert.Same(p.Resolve("Things").Set, p.Resolve("Thingies").Set);
        Assert.Null(p.Resolve("Nope").Set);
    }

    [Fact]
    public void UnavailableSetNamesTheMissingTable()
    {
        var p = ReadTestData.Pkg();
        var e = Assert.Throws<ReadException>(() => ReadPlanner.Plan(p, "Gone?$top=1"));
        Assert.Equal(400, e.Status);
        Assert.Contains("Not available in this version of SAP Business One (build 1.0.0.0-aaaaaaaaaaaa)", e.Message);
        Assert.Contains("table OGON is missing", e.Message);
    }

    [Theory]
    [InlineData("GET", "Things", null)]
    [InlineData("HEAD", "Things", null)]
    [InlineData("POST", "Things", 405)]
    [InlineData("PATCH", "Thingies", 405)]
    [InlineData("DELETE", "Pairs", 405)]
    [InlineData("POST", "Gone", 400)]
    [InlineData("POST", "BusinessPartners", null)]   // not a set of the package: not ours
    public void GateDecidesReadOnly(string method, string set, int? status) =>
        Assert.Equal(status, ReadGate.Decide(ReadTestData.Pkg(), method, set)?.Status);
}

public class ReadPlannerTests
{
    private static readonly ReadPackage P = ReadTestData.Pkg();
    private static ReadPlan Plan(string url, int ps = 20) => ReadPlanner.Plan(P, url, ps);
    private static string N(string s) => ReadTestData.Norm(s);

    [Fact]
    public void ListUsesAllColumnsInPhysicalOrderAndNoOrder() =>
        Assert.Equal("SELECT T0.[Code], T0.[Descr], T0.[KindCol], T0.[Qty], T0.[Small], T0.[DocTime], T0.[Price], T0.[Created], T0.[Memo], T0.[Internal] " +
                     "FROM [dbo].[OTHG] T0 ORDER BY CURRENT_TIMESTAMP OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY", N(Plan("Things").Main!.Sql));

    [Fact]
    public void ListPagesWithTopSkipAndPageSize()
    {
        Assert.EndsWith("OFFSET 1 ROWS FETCH NEXT 3 ROWS ONLY", Plan("Things?$top=3&$skip=1").Main!.Sql.Trim());
        Assert.EndsWith("OFFSET 4 ROWS FETCH NEXT 20 ROWS ONLY", Plan("Things?$skip=4").Main!.Sql.Trim());
        Assert.EndsWith("OFFSET 0 ROWS FETCH NEXT 2 ROWS ONLY", Plan("Things", 2).Main!.Sql.Trim());
        Assert.EndsWith("FETCH NEXT 20 ROWS ONLY", Plan("Things?$top=500").Main!.Sql.Trim());
        Assert.Equal(400, Assert.Throws<ReadException>(() => Plan("Things?$top=-1")).Status);
        Assert.Equal("299", Assert.Throws<ReadException>(() => Plan("Things?$top=0")).Code);
    }

    [Fact]
    public void SelectOrderbyUseColumnsOfTheProperties()
    {
        var pl = Plan("Things?$select=Quantity,Name&$orderby=Name desc,Quantity asc,Code&$top=2");
        Assert.Equal("SELECT T0.[Qty], T0.[Descr] FROM [dbo].[OTHG] T0 ORDER BY T0.[Descr] DESC,T0.[Qty],T0.[Code] OFFSET 0 ROWS FETCH NEXT 2 ROWS ONLY", N(pl.Main!.Sql));
        Assert.False(pl.IncludeChildren);
    }

    [Fact]
    public void InvalidPropertyIsTheNativeError()
    {
        foreach (var url in new[] { "Things?$select=Nope", "Things?$orderby=Nope", "Things?$filter=Nope eq 1", "Things?$select=Internal", "Things?$select=Memo" })
        {
            var e = Assert.Throws<ReadException>(() => Plan(url));
            Assert.Equal(("-1000", 400), (e.Code, e.Status));
            Assert.Contains("of 'Thing'is invalid", e.Message);
        }
    }

    [Fact]
    public void FilterIsAlwaysParameterized()
    {
        var pl = Plan("Things?$filter=Name eq 'x'' OR 1=1; DROP TABLE OTHG--' and Quantity gt -5 or Kind eq 'k_B'");
        Assert.Equal("(T0.[Descr] = (@P1) AND T0.[Qty] > (@P2) ) OR T0.[KindCol] = (@P3)",
            N(pl.Main!.Sql).Split("WHERE ")[1].Split(" ORDER BY")[0]);
        Assert.DoesNotContain("DROP", pl.Main.Sql);
        Assert.Equal("@P1 nvarchar(254),@P2 int,@P3 char(1)", pl.Main.Declarations);
        Assert.Equal("x' OR 1=1; DROP TABLE OTHG--", pl.Main.Params[0].Value);
        Assert.Equal(-5, pl.Main.Params[1].Value);
        Assert.Equal("B", pl.Main.Params[2].Value);   // enum: the database value, not the name
    }

    [Fact]
    public void FilterTypesAndLike()
    {
        Assert.Equal("@P1 smallint", Plan("Things?$filter=Small eq 3").Main!.Declarations);
        Assert.Equal("@P1 numeric(25,6)", Plan("Things?$filter=Price ge 1").Main!.Declarations);
        var d = Plan("Things?$filter=CreateDate gt 2012-01-01T00:00:00Z").Main!;
        Assert.Equal(("@P1 datetime2", new DateTime(2012, 1, 1)), (d.Declarations, d.Params[0].Value));
        var l = Plan("Things?$filter=contains(Name,'Road') and startswith(Name,'SAP') and endswith(Name,'Map')").Main!;
        Assert.Equal("@P1 nvarchar(258),@P2 nvarchar(257),@P3 nvarchar(257)", l.Declarations);
        Assert.Equal(new object[] { "%Road%", "SAP%", "%Map" }, l.Params.Select(p => p.Value).ToArray());
        Assert.Contains("T0.[Descr] Like @P1 AND T0.[Descr] Like @P2", N(l.Sql));
        var n = Plan("Things?$filter=Name eq null and not (Quantity eq 1)").Main!;
        Assert.Contains("WHERE T0.[Descr] IS NULL AND NOT (T0.[Qty] = (@P1) )", N(n.Sql));
    }

    [Theory]
    [InlineData("tolower(Name) eq 'x'", "Query string error - Not supported function")]
    [InlineData("Quantity in (1,2)", "Query string error - Not supported function")]
    [InlineData("length(Name) gt 1", "Query string error - Not supported function")]
    [InlineData("Name eq", "Query string error - Invalid symbol in the filter condition")]
    [InlineData("Name == 'x'", "Query string error - Invalid symbol in the filter condition")]
    public void UnsupportedFilterIsA400WithTheNativeText(string filter, string text)
    {
        var e = Assert.Throws<ReadException>(() => Plan("Things?$filter=" + filter));
        Assert.Equal((400, "201", text), (e.Status, e.Code, e.Message));
    }

    [Fact]
    public void BadEnumNameListsTheValidOnes()
    {
        var e = Assert.Throws<ReadException>(() => Plan("Things?$filter=Kind eq 'zzz'"));
        Assert.Equal(("-1013", "Invalid item name 'zzz' in Enum 'KindEnum'. The valid names are: 'k_A'-'A', 'k_B'-'B'"), (e.Code, e.Message));
    }

    [Fact]
    public void PendingAndUnmeasuredAreRefused()
    {
        Assert.Equal(501, Assert.Throws<ReadException>(() => Plan("Blobs?$select=Memo")).Status);
        Assert.Equal(501, Assert.Throws<ReadException>(() => Plan("Blobs?$filter=Memo eq 'x'")).Status);
        Assert.Equal(501, Assert.Throws<ReadException>(() => Plan("Things?$crossjoin=a")).Status);
        Assert.Equal(501, Assert.Throws<ReadException>(() => Plan("Things?$filter=CreateDate gt 2012-01-01T10:00:00Z")).Status);
        Assert.Equal(501, Assert.Throws<ReadException>(() => Plan("Things?$filter=substringof('x',Name)")).Status);
    }

    [Fact]
    public void ExpandOfAChildIsTheNativeError() =>
        Assert.Equal("Cannot expand invalid navigation property 'ThingLines' for entity type 'Thing'",
            Assert.Throws<ReadException>(() => Plan("Things?$expand=ThingLines")).Message);

    [Fact]
    public void CountRunsTheTwoStatements()
    {
        var pl = Plan("Things?$count=true&$top=1&$filter=Quantity gt 1");
        Assert.Equal(new[] { "SELECT COUNT('A') FROM [dbo].[OTHG] T0 WHERE T0.[Qty] > (@P1)", "SELECT COUNT(T0.[Code]) FROM [dbo].[OTHG] T0 WHERE T0.[Qty] > (@P1)" }, pl.Counts.Select(c => N(c.Sql)));
        Assert.True(pl.InlineCount);
        Assert.Equal(2, Plan("Things?$inlinecount=allpages").Counts.Count);
        var only = Plan("Things/$count");
        Assert.True(only.CountOnly);
        Assert.Null(only.Main);
        Assert.Equal("SELECT COUNT('A') FROM [dbo].[OTHG] T0", N(only.Counts[0].Sql));
    }

    [Fact]
    public void GetByKeyIsTypedOrderedAndNotPaged()
    {
        var pl = Plan("Things('A1')");
        Assert.Equal("SELECT T0.[Code], T0.[Descr], T0.[KindCol], T0.[Qty], T0.[Small], T0.[DocTime], T0.[Price], T0.[Created], T0.[Memo], T0.[Internal] FROM [dbo].[OTHG] T0 WHERE T0.[Code] = (@P1) ORDER BY T0.[Code]", N(pl.Main!.Sql));
        Assert.True(pl.Single && pl.Serializable && pl.IncludeChildren);
        Assert.Equal(("404", "-2028"), (pl.NotFound!.Status.ToString(), pl.NotFound.Code));
        Assert.Equal(1, Plan("Pairs(A=5,B='k_B')").Main!.Params.Count(p => p.Value is "B"));
        Assert.Equal("5", Plan("Pairs(5,'k_A')").Main!.Params[0].Value.ToString());
        Assert.Equal(("400", "203"), (Assert.Throws<ReadException>(() => Plan("Pairs('abc','k_A')")).Status.ToString(), "203"));
        Assert.Equal("203", Assert.Throws<ReadException>(() => Plan("Pairs('abc','k_A')")).Code);
    }

    [Fact]
    public void KeyWithSelectIsAPagedFilteredList()
    {
        var pl = Plan("Things('A1')?$select=Name");
        Assert.Equal("SELECT T0.[Descr] FROM [dbo].[OTHG] T0 WHERE T0.[Code] = (@P1) ORDER BY CURRENT_TIMESTAMP OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY", N(pl.Main!.Sql));
        Assert.Equal("-2028", pl.NotFound!.Code);
        Assert.Contains("navigation property has no content", pl.NotFound.Message);
        // composite key with an enum plus $select: rule of the package
        Assert.Equal("Invalid query option: key value is not matched with its type", Assert.Throws<ReadException>(() => Plan("Pairs(5,'k_A')?$select=S")).Message);
    }

    [Fact]
    public void FilterByWholeKeyTakesTheGetByKeyRoute()
    {
        var pl = Plan("Pairs?$filter=B eq 'k_A' and A eq 7&$top=5&$skip=1");
        Assert.Equal("SELECT T0.[A], T0.[B], T0.[S] FROM [dbo].[OPAI] T0 WHERE T0.[A] = (@P1) AND T0.[B] = (@P2) ORDER BY T0.[A],T0.[B]", N(pl.Main!.Sql));
        Assert.True(pl.Serializable);
        Assert.False(pl.Single);
        Assert.Null(pl.NextLink(20));
        Assert.Contains("OFFSET", Plan("Pairs?$filter=A eq 7").Main!.Sql);   // only part of the key: ordinary list
    }

    [Fact]
    public void ChildrenAreOneQueryPerRootRowWithThePackageJoinAndOrder()
    {
        var pl = Plan("Things");
        var q = pl.ChildQuery(pl.Set.Children[0], new object?[] { "A1", "n", "A", 1, 1, 1, 1m, null, null, null });
        Assert.Equal("SELECT T0.[Code], T0.[LineNum], T0.[Text] FROM [dbo].[THG1] T0 WHERE ((T0.[Code] = (@P1) )) ORDER BY T0.[Code],T0.[LineNum]", N(q.Sql));
        Assert.Equal("A1", q.Params[0].Value);
        Assert.Equal(501, Assert.Throws<ReadException>(() => pl.ChildQuery(pl.Set.Children[1], new object?[10])).Status);
        // $select of a collection reads the key and the children; a $select without it reads no children
        var sc = Plan("Things?$select=ThingLines&$top=1");
        Assert.Equal("SELECT T0.[Code] FROM [dbo].[OTHG] T0 ORDER BY CURRENT_TIMESTAMP OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY", N(sc.Main!.Sql));
        Assert.True(sc.IncludeChildren);
    }

    [Fact]
    public void UserFieldsAreAppendedAndAddressable()
    {
        var udf = new ReadColumn { Name = "U_x", SqlType = "nvarchar(30)" };
        IReadOnlyList<ReadColumn> U(string t) => t == "OTHG" ? new[] { udf } : Array.Empty<ReadColumn>();
        var pl = ReadPlanner.Plan(P, "Things?$filter=U_x eq 'a'&$orderby=U_x", 20, U);
        Assert.Contains("T0.[Internal], T0.[U_x] FROM", N(pl.Main!.Sql).Replace(" , ", ", "));
        Assert.Contains("WHERE T0.[U_x] = (@P1) ORDER BY T0.[U_x]", N(pl.Main.Sql));
        Assert.Throws<InvalidDataException>(() => ReadPlanner.Plan(P, "Things", 20, _ => new[] { new ReadColumn { Name = "U_x] --", SqlType = "int" } }));
    }

    [Theory]
    [InlineData("Things?$top=50", 20, 20, "Things?$top=30&$skip=20")]
    [InlineData("Things?$top=50&$skip=20", 20, 20, "Things?$top=30&$skip=40")]
    [InlineData("Things", 20, 20, "Things?$skip=20")]
    [InlineData("Things", 3, 3, "Things?$skip=3")]
    [InlineData("Things?$filter=Name eq 'a b'&$skip=2", 20, 20, "Things?$filter=Name%20eq%20'a%20b'&$skip=22")]
    [InlineData("Things?$top=20", 20, 20, null)]
    [InlineData("Things", 20, 19, null)]
    [InlineData("Things?$top=3", 20, 3, null)]
    public void NextLinkIsRelative(string url, int pageSize, int returned, string? next) => Assert.Equal(next, Plan(url, pageSize).NextLink(returned));
}

public class ReadJsonTests
{
    private static readonly ReadPackage P = ReadTestData.Pkg();

    private static string Json(string url, params object?[][] rows) =>
        ReadJson.Body(ReadPlanner.Plan(P, url), P, rows.Select(r => new ReadRow(r)).ToList(), "https://h/b1s/v2/$metadata#Things");

    // physical order: Code, Descr, KindCol, Qty, Small, DocTime, Price, Created, Memo, Internal
    private static object?[] Row(object? memo = null, object? created = null, string kind = "A", decimal price = 15660m) =>
        new object?[] { "A1", null, kind, 7, (short)2, (short)1736, price, created, memo, "hidden" };

    [Fact]
    public void ConvertsInEntityTypeOrder()
    {
        var plan = ReadPlanner.Plan(P, "Things?$select=CreateDate,Code,Quantity,DocTime,Price,Kind,Name");
        var body = ReadJson.Body(plan, P, new[] { new ReadRow(new object?[] { new DateTime(2024, 5, 31), "A1", 7, (short)1736, 15660m, "B", null }) }, "ctx");
        Assert.Equal("""{"@odata.context":"ctx","value":[{"Code":"A1","Name":null,"Kind":"k_B","Quantity":7,"DocTime":"17:36:00","Price":15660.0,"CreateDate":"2024-05-31T00:00:00Z"}]}""", body);
    }

    [Fact]
    public void PriceIsAJsoncppDouble()
    {
        var plan = ReadPlanner.Plan(P, "Things?$select=Price");
        string B(decimal v) => ReadJson.Body(plan, P, new[] { new ReadRow(new object?[] { v }) }, "c");
        Assert.Contains("\"Price\":0.66}", B(0.66m));
        Assert.Contains("\"Price\":1.265}", B(1.265m));
        Assert.Contains("\"Price\":0.0}", B(0m));
    }

    [Fact]
    public void ValueOutsideTheEnumOmitsTheProperty() =>
        Assert.Equal("""{"@odata.context":"ctx","value":[{"IdOnly":1}]}""".Replace("IdOnly", "Quantity"),
            ReadJson.Body(ReadPlanner.Plan(P, "Things?$select=Kind,Quantity"), P, new[] { new ReadRow(new object?[] { "Y", 1 }) }, "ctx"));

    [Fact]
    public void FullRowSkipsHiddenAndRefusesPending()
    {
        // all columns: Memo is exposed and its type is pending -> error, even when NULL
        Assert.Equal(501, Assert.Throws<ReadException>(() => Json("Blobs", new object?[] { 1, null })).Status);
        // a pending column that the entity does not expose is read (it is in the select list) and never written
        Assert.Contains("\"Code\":\"A1\"", Json("Things?$select=Code", new object?[] { "A1" }));
    }

    [Fact]
    public void DateWithATimeIsRefused()
    {
        var plan = ReadPlanner.Plan(P, "Things?$select=CreateDate");
        var e = Assert.Throws<ReadException>(() => ReadJson.Body(plan, P, new[] { new ReadRow(new object?[] { new DateTime(2024, 5, 31, 10, 0, 0) }) }, "c"));
        Assert.Contains("datetime-nonzero-time", e.Message);
    }

    [Fact]
    public void UserFieldsGoLastAndChildrenAfterTheRoot()
    {
        var p = ReadTestData.Pkg();
        p.Resolve("Things").Set!.Children.RemoveAt(1);   // the pending child would (rightly) refuse the JSON
        var udf = new ReadColumn { Name = "U_x", SqlType = "nvarchar(30)" };
        var plan = ReadPlanner.Plan(p, "Things?$select=ThingLines", 20, t => t == "OTHG" ? new[] { udf } : Array.Empty<ReadColumn>());
        var kids = new Dictionary<string, IReadOnlyList<object?[]>> { ["ThingLines"] = new[] { new object?[] { "A1", 1, "t" }, new object?[] { "A1", 2, null } } };
        Assert.Equal("""{"@odata.context":"c","value":[{"Code":"A1","ThingLines":[{"Code":"A1","LineNum":1,"Text":"t"},{"Code":"A1","LineNum":2,"Text":null}]}]}""",
            ReadJson.Body(plan, p, new[] { new ReadRow(new object?[] { "A1" }, kids) }, "c"));
        var all = ReadPlanner.Plan(p, "Things?$select=Code,U_x", 20, t => t == "OTHG" ? new[] { udf } : Array.Empty<ReadColumn>());
        Assert.Equal("""{"@odata.context":"c","value":[{"Code":"A1","U_x":null}]}""", ReadJson.Body(all, p, new[] { new ReadRow(new object?[] { "A1", null }) }, "c"));
    }

    [Fact]
    public void SingleEntityAndNotFound()
    {
        var plan = ReadPlanner.Plan(P, "Things('A1')?$select=Quantity");
        Assert.Equal("""{"@odata.context":"c/$entity","Quantity":7}""", ReadJson.Body(plan, P, new[] { new ReadRow(new object?[] { 7 }) }, "c"));
        Assert.Equal(404, Assert.Throws<ReadException>(() => ReadJson.Body(plan, P, Array.Empty<ReadRow>(), "c")).Status);
    }

    [Fact]
    public void CountAndNextLinkAppear()
    {
        var plan = ReadPlanner.Plan(P, "Things?$count=true&$select=Quantity", 2);
        var b = ReadJson.Body(plan, P, new[] { new ReadRow(new object?[] { 1 }), new ReadRow(new object?[] { 2 }) }, "c", 9);
        Assert.Equal("""{"@odata.count":9,"@odata.context":"c","value":[{"Quantity":1},{"Quantity":2}],"@odata.nextLink":"Things?$count=true&$select=Quantity&$skip=2"}""", b);
    }

    [Fact]
    public void WithoutPropertyOrderTheJsonIsRefused()
    {
        var p = ReadPackage.Load(new MemoryStream(Encoding.UTF8.GetBytes(ReadTestData.PackageJson.Replace("\"propertyOrder\": [\"Code\", \"Name\"", "\"x\": [\"Code\", \"Name\""))));
        var plan = ReadPlanner.Plan(p, "Things?$select=Code");
        Assert.Contains("property order", Assert.Throws<ReadException>(() => ReadJson.Body(plan, p, new[] { new ReadRow(new object?[] { "a" }) }, "c")).Message);
    }
}
