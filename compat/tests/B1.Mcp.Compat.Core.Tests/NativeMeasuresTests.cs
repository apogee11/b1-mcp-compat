using Xunit;
using B1.Mcp.Compat.Core.Read;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>What the native Service Layer FP2608 answered to read-only GETs (rea/01-findings 2026-10-27, "Grupo A"): page size, null filters, context url.</summary>
public class NativeMeasuresTests
{
    [Theory]
    [InlineData(null, null, 20, 20)]
    [InlineData("odata.maxpagesize=5", null, 20, 5)]
    [InlineData("odata.maxpagesize=50", "7", 20, 50)]       // Prefer wins over B1S-PageSize
    [InlineData(null, "7", 20, 7)]
    [InlineData(null, " 6 ", 20, 6)]
    [InlineData(null, "5.5", 20, 5)]
    [InlineData(null, "0", 20, PageSizeRule.Unlimited)]
    [InlineData(null, "-1", 20, PageSizeRule.Unlimited)]
    [InlineData("odata.maxpagesize=0", null, 20, PageSizeRule.Unlimited)]
    [InlineData("odata.maxpagesize=abc", null, 20, PageSizeRule.Unlimited)]
    [InlineData("odata.maxpagesize=", null, 20, PageSizeRule.Unlimited)]
    [InlineData("odata.maxpagesize=-1", null, 20, 20)]
    [InlineData("odata.maxpagesize = 4", null, 20, 4)]
    [InlineData("odata.maxpagesize=4;x=1", null, 20, 4)]
    [InlineData("odata.maxpagesize=4abc", null, 20, 4)]
    [InlineData("return=minimal, odata.maxpagesize=4", null, 20, 20)]   // a comma list is not recognized
    [InlineData("ODATA.MAXPAGESIZE=4", null, 20, 20)]                    // case sensitive
    [InlineData(null, null, 100, 100)]                                   // configured PageSize (b1s.conf mirror)
    public void PageSizeFollowsTheNativeRule(string? prefer, string? b1s, int configured, int expected) =>
        Assert.Equal(expected, PageSizeRule.Resolve(prefer, b1s, configured));

    [Fact]
    public void B1sPageSizeWithoutNumberIsTheNativeError()
    {
        var e = Assert.Throws<ReadException>(() => PageSizeRule.Resolve(null, "abc", 20));
        Assert.Equal((500, "402"), (e.Status, e.Code));
        Assert.Contains("'B1S-PageSize' is invalid", e.Message);
    }

    [Fact]
    public void UnlimitedPageNeverWritesANextLink()
    {
        var plan = ReadPlanner.Plan(ReadTestData.Pkg(), "Things", PageSizeRule.Unlimited);
        Assert.Null(plan.NextLink(20));
    }

    [Theory]
    [InlineData("Name eq null", "T0.[Descr] IS NULL")]
    [InlineData("Name ne null", "T0.[Descr] IS NOT NULL")]
    public void NullComparisonsAreIsNullAndIsNotNull(string filter, string sql) =>
        Assert.Contains(sql, ReadTestData.Norm(ReadPlanner.Plan(ReadTestData.Pkg(), "Things?$filter=" + filter).Main!.Sql));

    [Theory]
    [InlineData("lt")] [InlineData("le")] [InlineData("gt")] [InlineData("ge")]
    public void OrderingAgainstNullIsTheNative400(string op)
    {
        var e = Assert.Throws<ReadException>(() => ReadPlanner.Plan(ReadTestData.Pkg(), $"Things?$filter=Quantity {op} null"));
        Assert.Equal((400, "201", "Query string error - not allowed operation on 'null'"), (e.Status, e.Code, e.Message));
    }

    [Theory]
    [InlineData("Things?$select=Code,Name", "{\"@odata.context\":\"https://h/b1s/v2/$metadata#Things\"")]
    [InlineData("Things(Code='A1')?$select=Code,Name", "{\"@odata.context\":\"https://h/b1s/v2/$metadata#Things/$entity\"")]
    public async Task ContextUrlIgnoresSelect(string url, string start)
    {
        var db = new ReadServiceTests.FakeDb { Rows = _ => new[] { new object?[] { "A1", "n" } } };
        var r = await ReadService.GetAsync(ReadTestData.Pkg(), db, "CMP", url, "https://h/b1s/v2", 20, default);
        Assert.StartsWith(start, r.Body);
    }
}
