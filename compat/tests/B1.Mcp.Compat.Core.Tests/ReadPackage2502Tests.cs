using Xunit;
using B1.Mcp.Compat.Core.Read;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>The embedded FP2502 package (B11000_2502): loads, 132 served sets, the 8 unavailable ones, its build id, and no omitted columns.</summary>
public class ReadPackage2502Tests
{
    private static ReadPackage Load()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "compat", "src", "B1.Mcp.Compat.Host", "Resources", "read-B11000_2502.json.gz");
            if (File.Exists(p)) return ReadPackage.LoadGz(File.OpenRead(p));
        }
        throw new FileNotFoundException("read-B11000_2502.json.gz is not embedded in the Host resources");
    }

    [Fact]
    public void LoadsWithItsBuildId()
    {
        var pkg = Load();
        Assert.Equal("B11000_2502", pkg.Build.Package);
        Assert.Equal("10.0.280.0-2eff74abbfbb-9d2a66382a73-68f879d84009", pkg.Build.BuildId);
        Assert.Equal("1000280", pkg.Build.LoginVersion);
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
