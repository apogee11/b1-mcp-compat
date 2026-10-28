using Xunit;
using B1.Mcp.Compat.Core.Read;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>The embedded SP2605 package (B11000SP_2605): loads, 138 served sets, the 2 unavailable ones, its build id, and no omitted columns.</summary>
public class ReadPackage2605Tests
{
    private static ReadPackage Load()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "compat", "src", "B1.Mcp.Compat.Host", "Resources", "read-B11000SP_2605.json.gz");
            if (File.Exists(p)) return ReadPackage.LoadGz(File.OpenRead(p));
        }
        throw new FileNotFoundException("read-B11000SP_2605.json.gz is not embedded in the Host resources");
    }

    [Fact]
    public void LoadsWithItsBuildId()
    {
        var pkg = Load();
        Assert.Equal("B11000SP_2605", pkg.Build.Package);
        Assert.Equal("10.0.330.0-47b4ea54a506-216ffe3a90ed-825e572978fd", pkg.Build.BuildId);
        Assert.Equal("1000330", pkg.Build.LoginVersion);
    }

    [Fact]
    public void Serves138SetsAndLists2Unavailable()
    {
        var pkg = Load();
        Assert.Equal(138, pkg.Sets.Count);
        Assert.Equal(new[] { "ElsterRun", "ImportToExcels" },
            pkg.Unavailable.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Empty(pkg.Sets.Keys.Intersect(pkg.Unavailable.Keys));
    }

    [Fact]
    public void ServesWhatTheTablesOf2605AllowWithNoOmissions()
    {
        var pkg = Load();
        Assert.All(pkg.Sets.Values, s => Assert.Empty(s.OmittedColumns));
        Assert.Contains("Remarks", pkg.Sets["TaxDeclarationBoxes"].PropertyOrder!);
        Assert.Contains("IsSystemDefined", pkg.Sets["PersonalFieldsSetups"].PropertyOrder!);
        Assert.Contains("WHTRelCode", pkg.Sets["SpecificWTHAmountsService"].Columns.Select(c => c.Name));
        Assert.Contains("DTVId", pkg.Sets["ManualDistributionRules"].Columns.Select(c => c.Name));
        Assert.Contains("Regime", pkg.Sets["PointsOfIssues"].Columns.Select(c => c.Name));
        foreach (var n in new[] { "CAEAInformations", "DatevRuns", "DatevTransactionsDeliveriesAndServices", "ISDDocuments", "WTaxCategories", "WTaxRelationTypes" })
            Assert.Contains(n, pkg.Sets.Keys);
    }
}
