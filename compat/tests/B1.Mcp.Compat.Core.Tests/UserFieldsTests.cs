using Xunit;
using B1.Mcp.Compat.Core.Catalog;
using B1.Mcp.Compat.Core.Read;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>User fields (CUFD) in the read runtime: rule in <see cref="UserFields"/>, sources in rea/01 (2026-10-26).</summary>
public class UserFieldsTests
{
    private sealed class FakeDb : ICompanyDatabase
    {
        public List<ReadSqlQuery> Queries { get; } = new();
        public List<UserFieldRow> Udf { get; } = new();
        public List<string> UdfTables { get; } = new();
        public Func<ReadSqlQuery, IReadOnlyList<object?[]>> Rows { get; set; } = _ => Array.Empty<object?[]>();

        public Task<IReadOnlyList<UserFieldRow>> LoadUserFieldsAsync(string companyDb, IReadOnlyCollection<string> tables, CancellationToken ct)
        {
            UdfTables.AddRange(tables);
            return Task.FromResult<IReadOnlyList<UserFieldRow>>(Udf.Where(u => tables.Contains(u.TableId)).ToList());
        }

        public Task<IReadOnlyList<object?[]>> ReadRowsAsync(string companyDb, ReadSqlQuery query, int columns, CancellationToken ct)
        {
            Queries.Add(query);
            return Task.FromResult(Rows(query));
        }

        public Task<IReadOnlyList<PersonalFieldRow>> LoadPersonalFieldsAsync(string companyDb, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadUserTableColumnsAsync(string companyDb, CancellationToken ct) => throw new NotSupportedException();
    }

    // default physical type = what sys.columns shows for the combinations of the 2608 demo (645 user fields); a test passes its own to override
    private static UserFieldRow U(string table, int id, string alias, string type, string? edit, int size, string? physical = "default")
    {
        physical = physical != "default" ? physical : (type, size) switch
        {
            ("A", 1) => "char(1)",
            ("A", _) => $"nvarchar({size})",
            ("B", _) => "numeric(19,6)",
            ("D", _) => "datetime",
            ("M", _) => "ntext",
            ("N", _) => "int",
            _ => null,
        };
        return new(table, id, alias, type, edit, size, physical);
    }

    // the combinations measured on the 2608 demo (645 user fields): TypeID/EditType/SizeID to the physical type
    [Theory]
    [InlineData("A", null, 1, "char(1)")]
    [InlineData("A", null, 10, "nvarchar(10)")]
    [InlineData("A", " ", 100, "nvarchar(100)")]
    [InlineData("B", "S", 16, "numeric(19,6)")]
    [InlineData("D", null, 8, "datetime")]
    [InlineData("M", null, 8, "ntext")]
    [InlineData("N", null, 11, "int")]
    public void MeasuredCombinationsHaveTheirSqlType(string type, string? edit, int size, string sql)
    {
        var c = UserFields.Column(U("OTHG", 0, "X1", type, edit, size));
        Assert.Equal("U_X1", c.Name);
        Assert.Equal(sql, c.SqlType);
        Assert.Null(c.SqlTypePending);
        Assert.Equal("U_X1", c.Json!.Property);
    }

    // the JSON follows the engine letter (TypeID, and N+T), not the EditType: B is a double with any EditType, D with T stays a date
    [Theory]
    [InlineData("B", "P", 16, "numeric(19,6)")]
    [InlineData("B", "R", 16, "numeric(19,6)")]
    [InlineData("B", "Q", 16, "numeric(19,6)")]
    [InlineData("B", "%", 16, "numeric(19,6)")]
    [InlineData("B", "S", 16, "numeric(11,2)")]
    [InlineData("D", "T", 8, "datetime")]
    [InlineData("N", null, 6, "smallint")]
    [InlineData("N", "T", 6, "smallint")]
    [InlineData("A", "R", 30, "nvarchar(30)")]
    public void TheEngineLetterDecidesNotTheEditType(string type, string? edit, int size, string physical)
    {
        var c = UserFields.Column(U("OTHG", 0, "X1", type, edit, size, physical));
        Assert.Null(c.SqlTypePending);
        Assert.Equal(physical, c.SqlType);
        Assert.Equal(type == "N" && edit == "T" ? "Edm.TimeOfDay" : null, c.Json!.EdmType);
    }

    [Theory]
    [InlineData("D", "T", 8, "smallint")]   // a date letter on a non-date column: the engine reads a date from it, not measured
    [InlineData("B", "P", 16, "float")]
    [InlineData("N", null, 11, "bigint")]
    [InlineData("M", null, 8, "image")]
    [InlineData("A", null, 10, null)]       // no column in the table
    [InlineData("Z", null, 3, "int")]       // a letter the engine does not serialize
    public void CombinationsNotMeasuredStayPending(string type, string? edit, int size, string? physical)
    {
        var c = UserFields.Column(U("OTHG", 0, "X1", type, edit, size, physical));
        Assert.NotNull(c.SqlTypePending);
        Assert.Equal("", c.SqlType);
    }

    [Fact]
    public void UnsafeAliasIsRefused() =>
        Assert.Throws<InvalidDataException>(() => UserFields.Column(U("OTHG", 0, "X]; DROP", "A", null, 10)));

    [Fact]
    public void OnlyTheRowsOfTheTableInFieldIdOrder()
    {
        var rows = new[] { U("OTHG", 2, "B", "A", null, 10), U("OBLB", 0, "Z", "A", null, 10), U("OTHG", 0, "A", "A", null, 10) };
        Assert.Equal(new[] { "U_A", "U_B" }, UserFields.ColumnsOf(rows, "OTHG").Select(c => c.Name));
    }

    [Fact]
    public void AllColumnsListEndsWithTheUserFieldsInFieldIdOrder()
    {
        var rows = new[] { U("OTHG", 1, "SECOND", "A", null, 10), U("OTHG", 0, "FIRST", "M", null, 8) };
        var plan = ReadPlanner.Plan(ReadTestData.Pkg(), "Things?$top=1", 20, t => UserFields.ColumnsOf(rows, t));
        var sql = ReadTestData.Norm(plan.Main!.Sql);
        Assert.Contains("T0.[Internal], T0.[U_FIRST], T0.[U_SECOND] FROM", sql);   // after the columns of the package
        Assert.Equal(ReadTestData.Pkg().Resolve("Things").Set!.Columns.Count + 2, plan.Columns.Count);
    }

    [Fact]
    public async Task SelectedUserFieldsAreReadAndWrittenByTheirType()
    {
        var db = new FakeDb { Rows = q => new[] { new object?[] { "A1", "hello", new DateTime(2020, 1, 2), 1.5m, 7, null } } };
        db.Udf.Add(U("OTHG", 0, "TXT", "A", null, 10));
        db.Udf.Add(U("OTHG", 1, "DAT", "D", null, 8));
        db.Udf.Add(U("OTHG", 2, "NUM", "B", "S", 16));
        db.Udf.Add(U("OTHG", 3, "INT", "N", null, 11));
        db.Udf.Add(U("OTHG", 4, "NIL", "A", null, 5));
        var r = await ReadService.GetAsync(ReadTestData.Pkg(), db, "CMP", "Things?$select=Code,U_TXT,U_DAT,U_NUM,U_INT,U_NIL&$top=1", "https://h/b1s/v2", 20, default);
        Assert.True(r.Status == 200, r.Body);
        Assert.Contains("""{"Code":"A1","U_TXT":"hello","U_DAT":"2020-01-02T00:00:00Z","U_NUM":1.5,"U_INT":7,"U_NIL":null}""", r.Body);
        Assert.Contains("T0.[Code], T0.[U_TXT], T0.[U_DAT], T0.[U_NUM], T0.[U_INT], T0.[U_NIL]", db.Queries[0].Sql);
        Assert.Equal(new[] { "OTHG", "THG1", "THG2" }, db.UdfTables);   // the table of the set and of its children, nothing else
    }

    [Fact]
    public async Task UserFieldOfAnotherTableIsNotApplied()
    {
        var db = new FakeDb();
        db.Udf.Add(U("OBLB", 0, "OTHER", "A", null, 10));
        var r = await ReadService.GetAsync(ReadTestData.Pkg(), db, "CMP", "Things?$select=Code,U_OTHER&$top=1", "https://h/b1s/v2", 20, default);
        Assert.Equal(400, r.Status);
        Assert.Contains("U_OTHER", r.Body);
    }

    [Fact]
    public async Task UserFieldWithAnUnmeasuredTypeIsAnErrorWhenRead()
    {
        var db = new FakeDb();
        db.Udf.Add(U("OTHG", 0, "TIM", "D", "T", 8, "smallint"));
        var r = await ReadService.GetAsync(ReadTestData.Pkg(), db, "CMP", "Things?$select=Code,U_TIM&$top=1", "https://h/b1s/v2", 20, default);
        Assert.Equal(501, r.Status);
        Assert.Contains("udf-type-D-edit-T-size-8-physical-smallint", r.Body);
        Assert.Empty(db.Queries);
        // not read: the rest of the columns still work
        var ok = await ReadService.GetAsync(ReadTestData.Pkg(), new FakeDb { Rows = _ => new[] { new object?[] { "A1" } } }, "CMP", "Things?$select=Code&$top=1", "https://h/b1s/v2", 20, default);
        Assert.Equal(200, ok.Status);
    }

    [Fact]
    public void ChildrenReadTheirUserFieldsAfterTheirColumns()
    {
        var rows = new[] { U("THG1", 0, "LTXT", "A", null, 10) };
        var plan = ReadPlanner.Plan(ReadTestData.Pkg(), "Things('A1')", 20, t => UserFields.ColumnsOf(rows, t));
        var child = plan.Set.Children.Single(c => c.Collection == "ThingLines");
        Assert.Equal(new[] { "Code", "LineNum", "Text", "U_LTXT" }, child.Columns.Select(c => c.Name));
        Assert.Equal(new[] { "Code", "LineNum", "Text", "U_LTXT" }, child.PropertyOrder);
        var q = plan.ChildQuery(child, new object?[plan.Columns.Count].Select((_, i) => i == 0 ? "A1" : null).ToArray());
        Assert.StartsWith("SELECT T0.[Code], T0.[LineNum], T0.[Text], T0.[U_LTXT] FROM [dbo].[THG1] T0", q.Sql);
    }

    [Fact]
    public void WithoutUserFieldsThePlanIsTheSame()
    {
        var a = ReadPlanner.Plan(ReadTestData.Pkg(), "Things?$top=1");
        var b = ReadPlanner.Plan(ReadTestData.Pkg(), "Things?$top=1", 20, _ => Array.Empty<ReadColumn>());
        Assert.Equal(a.Main!.Sql, b.Main!.Sql);
    }
}
