using Xunit;
using B1.Mcp.Compat.Core.Read;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>The embedded SP2311 package (B11000SP_2311): loads, 127 served sets, the 13 unavailable ones, its build id, the 2 omitted columns and the DeductibleTaxs alias.</summary>
public class ReadPackage2311Tests
{
    private static ReadPackage Load()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "compat", "src", "B1.Mcp.Compat.Host", "Resources", "read-B11000SP_2311.json.gz");
            if (File.Exists(p)) return ReadPackage.LoadGz(File.OpenRead(p));
        }
        throw new FileNotFoundException("read-B11000SP_2311.json.gz is not embedded in the Host resources");
    }

    [Fact]
    public void LoadsWithItsBuildId()
    {
        var pkg = Load();
        Assert.Equal("B11000SP_2311", pkg.Build.Package);
        Assert.Equal("10.0.230.0-678fafff8d3b-91016ef69a32-247035d964c4", pkg.Build.BuildId);
        Assert.Equal("1000230", pkg.Build.LoginVersion);
    }

    [Fact]
    public void Serves127SetsAndLists13Unavailable()
    {
        var pkg = Load();
        Assert.Equal(127, pkg.Sets.Count);
        Assert.Equal(new[] { "ATDocumentTypes", "CAEAInformations", "DatevRuns", "DatevTransactionsDeliveriesAndServices", "ElsterRun", "ISDDocuments", "ImportToExcels", "PersonalDataRetentionPeriods", "SAFTProductTypes", "SAFTTaxCodes", "SAFTTransactionTypes", "WTaxCategories", "WTaxRelationTypes" },
            pkg.Unavailable.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Empty(pkg.Sets.Keys.Intersect(pkg.Unavailable.Keys));
    }

    [Fact]
    public void OmitsTheTwoColumnsThatThisBuildLacks()
    {
        var pkg = Load();
        Assert.Equal(new[] { "PersonalFieldsSetups", "TaxDeclarationBoxes" },
            pkg.Sets.Where(kv => kv.Value.OmittedColumns.Count > 0).Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "SysDefined" }, pkg.Sets["PersonalFieldsSetups"].OmittedColumns);
        Assert.Equal(new[] { "Remarks" }, pkg.Sets["TaxDeclarationBoxes"].OmittedColumns);
        Assert.DoesNotContain("Remarks", pkg.Sets["TaxDeclarationBoxes"].PropertyOrder!);
        Assert.DoesNotContain("IsSystemDefined", pkg.Sets["PersonalFieldsSetups"].PropertyOrder!);
    }

    [Fact]
    public void KeepsTheDeductibleTaxsAlias()
    {
        var pkg = Load();
        Assert.Contains("DeductibleTaxs", pkg.Sets["DeductibleTaxes"].Aliases);
    }
}
