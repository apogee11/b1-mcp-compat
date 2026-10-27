using Xunit;
using B1.Mcp.Compat.Core.Read;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>The embedded FP2208 package (B11000_2208): loads, 121 served sets, the 19 unavailable ones, its build id, and the two omitted columns.</summary>
public class ReadPackage2208Tests
{
    private static ReadPackage Load()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "compat", "src", "B1.Mcp.Compat.Host", "Resources", "read-B11000_2208.json.gz");
            if (File.Exists(p)) return ReadPackage.LoadGz(File.OpenRead(p));
        }
        throw new FileNotFoundException("read-B11000_2208.json.gz is not embedded in the Host resources");
    }

    [Fact]
    public void LoadsWithItsBuildId()
    {
        var pkg = Load();
        Assert.Equal("B11000_2208", pkg.Build.Package);
        Assert.Equal("10.0.200.0-24aeb23de137-2eb48c85224f-c15ab35d594d", pkg.Build.BuildId);
        Assert.Equal("1000200", pkg.Build.LoginVersion);
    }

    [Fact]
    public void Serves121SetsAndLists19Unavailable()
    {
        var pkg = Load();
        Assert.Equal(121, pkg.Sets.Count);
        Assert.Equal(new[] { "ATDocumentTypes", "CAEAInformations", "DatevRuns", "DatevTransactionsDeliveriesAndServices", "DeductibleTaxes", "EBooksFeesCategories", "EBooksOtherTaxCategories", "EBooksRecordTypes", "EBooksStampDutyCategories", "ElsterRun", "ISDDocuments", "ImportToExcels", "OfficialAccountCodes", "PersonalDataRetentionPeriods", "SAFTProductTypes", "SAFTTaxCodes", "SAFTTransactionTypes", "WTaxCategories", "WTaxRelationTypes" },
            pkg.Unavailable.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Empty(pkg.Sets.Keys.Intersect(pkg.Unavailable.Keys));
    }

    [Fact]
    public void OmitsOnlyTheTwoColumnsAbsentFromThe2208Metadata()
    {
        var pkg = Load();
        var omitting = pkg.Sets.Where(kv => kv.Value.OmittedColumns.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value.OmittedColumns.ToArray());
        Assert.Equal(new[] { "PersonalFieldsSetups", "TaxDeclarationBoxes" }, omitting.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "SysDefined" }, omitting["PersonalFieldsSetups"]);
        Assert.Equal(new[] { "Remarks" }, omitting["TaxDeclarationBoxes"]);
        Assert.DoesNotContain("Remarks", pkg.Sets["TaxDeclarationBoxes"].PropertyOrder!);
    }
}
