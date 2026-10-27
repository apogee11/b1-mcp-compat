using Xunit;
using B1.Mcp.Compat.Core.Read;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>The embedded FP2505 package (B11000SP_2505): loads, 132 served sets, the 8 unavailable ones, its build id, and no omitted columns.</summary>
public class ReadPackage2505Tests
{
    private static ReadPackage Load()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "compat", "src", "B1.Mcp.Compat.Host", "Resources", "read-B11000SP_2505.json.gz");
            if (File.Exists(p)) return ReadPackage.LoadGz(File.OpenRead(p));
        }
        throw new FileNotFoundException("read-B11000SP_2505.json.gz is not embedded in the Host resources");
    }

    [Fact]
    public void LoadsWithItsBuildId()
    {
        var pkg = Load();
        Assert.Equal("B11000SP_2505", pkg.Build.Package);
        Assert.Equal("10.0.290.0-c9ae21daeed4-55fe44b82e4d-3443b8dca453", pkg.Build.BuildId);
        Assert.Equal("1000290", pkg.Build.LoginVersion);
    }

    [Fact]
    public void Serves132SetsAndLists8Unavailable()
    {
        var pkg = Load();
        Assert.Equal(132, pkg.Sets.Count);
        Assert.Equal(new[] { "CAEAInformations", "DatevRuns", "DatevTransactionsDeliveriesAndServices", "ElsterRun", "ISDDocuments", "ImportToExcels", "WTaxCategories", "WTaxRelationTypes" },
            pkg.Unavailable.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Empty(pkg.Sets.Keys.Intersect(pkg.Unavailable.Keys));
    }

    [Fact]
    public void ServesThe2408GapsWithoutOmittingColumns()
    {
        var pkg = Load();
        Assert.Contains("PersonalDataRetentionPeriods", pkg.Sets.Keys);
        Assert.All(pkg.Sets.Values, s => Assert.Empty(s.OmittedColumns));
        Assert.Contains("Remarks", pkg.Sets["TaxDeclarationBoxes"].PropertyOrder!);
        Assert.Contains("IsSystemDefined", pkg.Sets["PersonalFieldsSetups"].PropertyOrder!);
    }
}
