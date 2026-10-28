using Xunit;
using B1.Mcp.Compat.Core.Read;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>The embedded SP2511 package (B11000SP_2511): loads, 135 served sets, the 5 unavailable ones, its build id, and no omitted columns.</summary>
public class ReadPackage2511Tests
{
    private static ReadPackage Load()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "compat", "src", "B1.Mcp.Compat.Host", "Resources", "read-B11000SP_2511.json.gz");
            if (File.Exists(p)) return ReadPackage.LoadGz(File.OpenRead(p));
        }
        throw new FileNotFoundException("read-B11000SP_2511.json.gz is not embedded in the Host resources");
    }

    [Fact]
    public void LoadsWithItsBuildId()
    {
        var pkg = Load();
        Assert.Equal("B11000SP_2511", pkg.Build.Package);
        Assert.Equal("10.0.310.0-ee6913dffe5b-7b4838227ade-e4634e8a197c", pkg.Build.BuildId);
        Assert.Equal("1000310", pkg.Build.LoginVersion);
    }

    [Fact]
    public void Serves135SetsAndLists5Unavailable()
    {
        var pkg = Load();
        Assert.Equal(135, pkg.Sets.Count);
        Assert.Equal(new[] { "DatevRuns", "DatevTransactionsDeliveriesAndServices", "ElsterRun", "ISDDocuments", "ImportToExcels" },
            pkg.Unavailable.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Empty(pkg.Sets.Keys.Intersect(pkg.Unavailable.Keys));
    }

    [Fact]
    public void ServesWhatTheTablesOf2511AllowWithNoOmissions()
    {
        var pkg = Load();
        Assert.All(pkg.Sets.Values, s => Assert.Empty(s.OmittedColumns));
        Assert.Contains("Remarks", pkg.Sets["TaxDeclarationBoxes"].PropertyOrder!);
        Assert.Contains("IsSystemDefined", pkg.Sets["PersonalFieldsSetups"].PropertyOrder!);
        Assert.Contains("WHTRelCode", pkg.Sets["SpecificWTHAmountsService"].Columns.Select(c => c.Name));
        Assert.Contains("Regime", pkg.Sets["PointsOfIssues"].Columns.Select(c => c.Name));
        // ODTV does not exist in 2511: the set is served without the DTVId column.
        Assert.DoesNotContain("DTVId", pkg.Sets["ManualDistributionRules"].Columns.Select(c => c.Name));
        foreach (var n in new[] { "CAEAInformations", "WTaxCategories", "WTaxRelationTypes" })
            Assert.Contains(n, pkg.Sets.Keys);
    }
}
