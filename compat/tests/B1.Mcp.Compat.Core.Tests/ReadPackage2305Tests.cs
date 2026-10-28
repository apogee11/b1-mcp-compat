using Xunit;
using B1.Mcp.Compat.Core.Read;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>The embedded FP2305 package (B11000_2305): loads, 126 served sets, the 14 unavailable ones, its build id, the 2 omitted columns and the DeductibleTaxs alias.</summary>
public class ReadPackage2305Tests
{
    private static ReadPackage Load()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "compat", "src", "B1.Mcp.Compat.Host", "Resources", "read-B11000_2305.json.gz");
            if (File.Exists(p)) return ReadPackage.LoadGz(File.OpenRead(p));
        }
        throw new FileNotFoundException("read-B11000_2305.json.gz is not embedded in the Host resources");
    }

    [Fact]
    public void LoadsWithItsBuildId()
    {
        var pkg = Load();
        Assert.Equal("B11000_2305", pkg.Build.Package);
        Assert.Equal("10.0.210.0-86df99d89132-3aade838ff2c-bafcc6c5b722", pkg.Build.BuildId);
        Assert.Equal("1000210", pkg.Build.LoginVersion);
    }

    [Fact]
    public void Serves126SetsAndLists14Unavailable()
    {
        var pkg = Load();
        Assert.Equal(126, pkg.Sets.Count);
        Assert.Equal(new[] { "ATDocumentTypes", "CAEAInformations", "DatevRuns", "DatevTransactionsDeliveriesAndServices", "ElsterRun", "ISDDocuments", "ImportToExcels", "OfficialAccountCodes", "PersonalDataRetentionPeriods", "SAFTProductTypes", "SAFTTaxCodes", "SAFTTransactionTypes", "WTaxCategories", "WTaxRelationTypes" },
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
