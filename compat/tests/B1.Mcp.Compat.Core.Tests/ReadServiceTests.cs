using Xunit;
using B1.Mcp.Compat.Core.Catalog;
using B1.Mcp.Compat.Core.Read;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>Routing (what is intercepted) and execution (plan, rows, JSON, errors) of the extended read, over the synthetic package and a fake database: no SQL Server involved.</summary>
public class ReadServiceTests
{
    internal sealed class FakeDb : ICompanyDatabase
    {
        public List<ReadSqlQuery> Queries { get; } = new();
        public Func<ReadSqlQuery, IReadOnlyList<object?[]>> Rows { get; set; } = _ => Array.Empty<object?[]>();

        public Task<IReadOnlyList<object?[]>> ReadRowsAsync(string companyDb, ReadSqlQuery query, int columns, CancellationToken ct)
        {
            Queries.Add(query);
            return Task.FromResult(Rows(query));
        }

        public List<UserFieldRow> Udf { get; } = new();
        public List<string> UdfTables { get; } = new();

        public Task<IReadOnlyList<UserFieldRow>> LoadUserFieldsAsync(string companyDb, IReadOnlyCollection<string> tables, CancellationToken ct)
        {
            UdfTables.AddRange(tables);
            return Task.FromResult<IReadOnlyList<UserFieldRow>>(Udf.Where(u => tables.Contains(u.TableId)).ToList());
        }

        public Task<IReadOnlyList<PersonalFieldRow>> LoadPersonalFieldsAsync(string companyDb, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadUserTableColumnsAsync(string companyDb, CancellationToken ct) => throw new NotSupportedException();
    }

    private static readonly Func<string, bool> SlHasNothing = _ => false;

    private static ReadRouteResult Route(string method, string path, Func<string, bool>? sl = null, ReadPackage? pkg = null) =>
        ReadRoute.Decide(pkg ?? ReadTestData.Pkg(), sl ?? SlHasNothing, method, path);

    [Theory]
    [InlineData("/b1s/v2/Things")]
    [InlineData("/b1s/v2/Things('A1')")]
    [InlineData("/b1s/v2/Things/$count")]
    [InlineData("/b1s/v2/Thingies")]   // alias
    [InlineData("/b1s/v2/Gone")]       // not available in the build: served as the error with its table
    public void SetsOfThePackageTheSlDoesNotExposeAreServed(string path) =>
        Assert.Equal(ReadAction.Serve, Route("GET", path).Action);

    [Theory]
    [InlineData("Things", "Thingies")]
    [InlineData("Thingies", "Things")]
    public void ASetTheSlAlreadyServesIsNeverInterceptedUnderThatExactName(string slName, string other)
    {
        // the SL exposes the name it exposes: neither GET nor writes on that name are ours; the other name of the same set is judged on its own
        foreach (var path in new[] { $"/b1s/v2/{slName}", $"/b1s/v2/{slName}('A1')" })
        {
            Assert.Equal(ReadAction.Passthrough, Route("GET", path, n => n == slName).Action);
            Assert.Equal(ReadAction.Passthrough, Route("POST", path, n => n == slName).Action);
        }
        Assert.Equal(ReadAction.Serve, Route("GET", $"/b1s/v2/{other}", n => n == slName).Action);
    }

    [Fact]
    public void WithoutPackageNothingIsIntercepted()
    {
        Assert.Equal(ReadAction.Passthrough, ReadRoute.Decide(null, SlHasNothing, "GET", "/b1s/v2/Things").Action);
        Assert.Equal(ReadAction.Passthrough, ReadRoute.Decide(null, SlHasNothing, "POST", "/b1s/v2/Things").Action);
    }

    [Theory]
    [InlineData("/b1s/v2/Orders")]            // not in the package
    [InlineData("/b1s/v1/Things")]            // v1: passthrough without logic
    [InlineData("/b1s/v2/$metadata")]
    [InlineData("/b1s/v2/Login")]
    [InlineData("/other/Things")]
    public void WhatIsNotASetOfThePackageOnV2PassesThrough(string path) =>
        Assert.Equal(ReadAction.Passthrough, Route("GET", path).Action);

    [Theory]
    [InlineData("POST")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void WritesOnOurSetsAreRefused(string method)
    {
        var r = Route(method, "/b1s/v2/Things('A1')");
        Assert.Equal(ReadAction.Reject, r.Action);
        Assert.Equal(405, r.Error!.Status);
        Assert.Contains("read only", r.Error.Message);
        Assert.Equal(ReadAction.Reject, Route(method, "/b1s/v2/Thingies").Action);
    }

    [Fact]
    public void WritesOnAnUnavailableSetGiveTheUnavailableError()
    {
        var r = Route("POST", "/b1s/v2/Gone");
        Assert.Equal(400, r.Error!.Status);
        Assert.Contains("OGON", r.Error.Message);
    }

    [Fact]
    public async Task ListIsPlannedRunAndWrittenAsODataJson()
    {
        var db = new FakeDb { Rows = q => q.Sql.Contains("OFFSET") ? new[] { new object?[] { "A1", "first" }, new object?[] { "A2", null } } : Array.Empty<object?[]>() };
        var r = await ReadService.GetAsync(ReadTestData.Pkg(), db, "CMP", "Things?$select=Code,Name&$top=2", "https://h:1/b1s/v2", 20, default);
        Assert.Equal(200, r.Status);
        Assert.Equal(ReadService.JsonType, r.ContentType);
        Assert.Equal("""{"@odata.context":"https://h:1/b1s/v2/$metadata#Things","value":[{"Code":"A1","Name":"first"},{"Code":"A2","Name":null}]}""", r.Body);
        var q = Assert.Single(db.Queries);
        Assert.StartsWith("SELECT ", q.Sql);
        Assert.Contains("[dbo].[OTHG]", q.Sql);
    }

    [Fact]
    public async Task AliasIsServedFromTheSameTable()
    {
        var db = new FakeDb();
        var r = await ReadService.GetAsync(ReadTestData.Pkg(), db, "CMP", "Thingies?$select=Code", "https://h/b1s/v2", 20, default);
        Assert.Equal(200, r.Status);
        Assert.Contains("[dbo].[OTHG]", db.Queries[0].Sql);
        Assert.Contains("$metadata#Thingies", r.Body);
    }

    [Fact]
    public async Task CountPathAnswersPlainText()
    {
        var db = new FakeDb { Rows = _ => new[] { new object?[] { 7 } } };
        var r = await ReadService.GetAsync(ReadTestData.Pkg(), db, "CMP", "Things/$count", "https://h/b1s/v2", 20, default);
        Assert.Equal((200, "7"), (r.Status, r.Body));
        Assert.StartsWith("text/plain", r.ContentType);
    }

    [Fact]
    public async Task UnavailableSetAnswersTheErrorWithItsTableAndNeverTouchesTheDatabase()
    {
        var db = new FakeDb();
        var r = await ReadService.GetAsync(ReadTestData.Pkg(), db, "CMP", "Gone", "https://h/b1s/v2", 20, default);
        Assert.Equal(400, r.Status);
        Assert.Contains("table OGON is missing", r.Body);
        Assert.Empty(db.Queries);
    }

    [Fact]
    public async Task ReadErrorsBecomeODataErrors()
    {
        var db = new FakeDb();
        var bad = await ReadService.GetAsync(ReadTestData.Pkg(), db, "CMP", "Things?$filter=Nope eq 1", "https://h/b1s/v2", 20, default);
        Assert.Equal(400, bad.Status);
        Assert.Contains("\"error\"", bad.Body);
        var notMeasured = await ReadService.GetAsync(ReadTestData.Pkg(), db, "CMP", "Things?$apply=x", "https://h/b1s/v2", 20, default);
        Assert.Equal(501, notMeasured.Status);
        Assert.Empty(db.Queries);
    }

    [Fact]
    public async Task SingleWithoutRowIs404()
    {
        var r = await ReadService.GetAsync(ReadTestData.Pkg(), new FakeDb(), "CMP", "Blobs(1)?$select=Id", "https://h/b1s/v2", 20, default);
        Assert.Equal(404, r.Status);
    }
}
