using Xunit;
using B1.Mcp.Compat.Core.Personal;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>The table the native action asks OPFS for comes from each release's own engine (maintainer tooling).</summary>
public class PersonalTableMapTests
{
    [Theory]
    [InlineData("B11000_2208", "RDR1", "DOC1", "R")]
    [InlineData("B11000SP_2408", "OCRD", "OCRD", "N")]
    [InlineData("B11000_2608", "OPCH", "ODOC", "P")]
    [InlineData("B11000_2608", "OTSI", "OTXI", "J")]
    [InlineData("B11000SP_2408", "TPI1", "TXI1", "K")]
    [InlineData("B11000_2208", "OTSI", "OTSI", "J")]   // FP2208 does not fold tax invoices into OTXI
    public void ResolvesWithTheMapOfTheRelease(string release, string table, string opfsTable, string category) =>
        Assert.Equal((opfsTable, category), PersonalTableMap.Resolve(table, release));

    [Fact]
    public void UnknownReleaseFailsClosed() => Assert.Throws<NotSupportedException>(() => PersonalTableMap.Resolve("ORDR", "9999"));
}
