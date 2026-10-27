using Xunit;
using B1.Mcp.Compat.Core.Read;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>The embedded FP2602 package (B11000_2602): loads, 138 served sets, the 2 unavailable ones, its build id, and no omitted columns.</summary>
public class ReadPackage2602Tests
{
    private static ReadPackage Load()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "compat", "src", "B1.Mcp.Compat.Host", "Resources", "read-B11000_2602.json.gz");
            if (File.Exists(p)) return ReadPackage.LoadGz(File.OpenRead(p));
        }
        throw new FileNotFoundException("read-B11000_2602.json.gz is not embedded in the Host resources");
    }

    [Fact]
    public void LoadsWithItsBuildId()
    {
        var pkg = Load();
        Assert.Equal("B11000_2602", pkg.Build.Package);
        Assert.Equal("10.0.320.0-1dbf7e7cfc75-a13b197811f4-92a0ab6f1a33", pkg.Build.BuildId);
        Assert.Equal("1000320", pkg.Build.LoginVersion);
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
    public void ServesTheGapsOf2505WithNewerColumnsAndNoOmissions()
    {
        var pkg = Load();
        Assert.Contains("PersonalDataRetentionPeriods", pkg.Sets.Keys);
        Assert.All(pkg.Sets.Values, s => Assert.Empty(s.OmittedColumns));
        Assert.Contains("Remarks", pkg.Sets["TaxDeclarationBoxes"].PropertyOrder!);
        Assert.Contains("IsSystemDefined", pkg.Sets["PersonalFieldsSetups"].PropertyOrder!);
        // physical columns that 2602 has and 2505 lacks
        Assert.Contains("WHTRelCode", pkg.Sets["SpecificWTHAmountsService"].Columns.Select(c => c.Name));
        Assert.Contains("DTVId", pkg.Sets["ManualDistributionRules"].Columns.Select(c => c.Name));
        Assert.Contains("Regime", pkg.Sets["PointsOfIssues"].Columns.Select(c => c.Name));
        // the 6 sets that 2505 does not serve are served here
        foreach (var n in new[] { "CAEAInformations", "DatevRuns", "DatevTransactionsDeliveriesAndServices", "ISDDocuments", "WTaxCategories", "WTaxRelationTypes" })
            Assert.Contains(n, pkg.Sets.Keys);
    }
}
