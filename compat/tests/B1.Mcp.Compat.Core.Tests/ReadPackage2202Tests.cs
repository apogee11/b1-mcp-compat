using Xunit;
using B1.Mcp.Compat.Core.Read;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>The embedded FP2202 package (B11000_2202): loads, 121 served sets, the 19 unavailable ones, its build id, and the two omitted columns.</summary>
public class ReadPackage2202Tests
{
    private static ReadPackage Load()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "compat", "src", "B1.Mcp.Compat.Host", "Resources", "read-B11000_2202.json.gz");
            if (File.Exists(p)) return ReadPackage.LoadGz(File.OpenRead(p));
        }
        throw new FileNotFoundException("read-B11000_2202.json.gz is not embedded in the Host resources");
    }

    [Fact]
    public void LoadsWithItsBuildId()
    {
        var pkg = Load();
        Assert.Equal("B11000_2202", pkg.Build.Package);
        Assert.Equal("10.0.190.0-16386a021d32-3d5447646399-acc3726c1513", pkg.Build.BuildId);
        Assert.Equal("1000190", pkg.Build.LoginVersion);
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
    public void OmitsOnlyTheTwoColumnsAbsentFromThe2202Metadata()
    {
        var pkg = Load();
        var omitting = pkg.Sets.Where(kv => kv.Value.OmittedColumns.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value.OmittedColumns.ToArray());
        Assert.Equal(new[] { "PersonalFieldsSetups", "TaxDeclarationBoxes" }, omitting.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "SysDefined" }, omitting["PersonalFieldsSetups"]);
        Assert.Equal(new[] { "Remarks" }, omitting["TaxDeclarationBoxes"]);
        Assert.DoesNotContain("Remarks", pkg.Sets["TaxDeclarationBoxes"].PropertyOrder!);
    }
}
