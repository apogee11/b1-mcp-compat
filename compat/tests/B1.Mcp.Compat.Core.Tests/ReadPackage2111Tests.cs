using Xunit;
using B1.Mcp.Compat.Core.Read;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>The embedded FP2111 package (B11000_2111): loads, 120 served sets, the 20 unavailable ones, its build id, the two sets with omitted columns and the date-only JSON.</summary>
public class ReadPackage2111Tests
{
    private static ReadPackage Load()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "compat", "src", "B1.Mcp.Compat.Host", "Resources", "read-B11000_2111.json.gz");
            if (File.Exists(p)) return ReadPackage.LoadGz(File.OpenRead(p));
        }
        throw new FileNotFoundException("read-B11000_2111.json.gz is not embedded in the Host resources");
    }

    [Fact]
    public void LoadsWithItsBuildId()
    {
        var pkg = Load();
        Assert.Equal("B11000_2111", pkg.Build.Package);
        Assert.Equal("10.0.180.0-aa308db3b547-227cb5ad83ae-4ff91d5b89ef", pkg.Build.BuildId);
        Assert.Equal("1000180", pkg.Build.LoginVersion);
    }

    [Fact]
    public void Serves120SetsAndLists20Unavailable()
    {
        var pkg = Load();
        Assert.Equal(120, pkg.Sets.Count);
        Assert.Equal(new[] { "ATDocumentTypes", "BlockExecutables", "CAEAInformations", "DatevRuns", "DatevTransactionsDeliveriesAndServices", "DeductibleTaxes", "EBooksFeesCategories", "EBooksOtherTaxCategories", "EBooksRecordTypes", "EBooksStampDutyCategories", "ElsterRun", "ISDDocuments", "ImportToExcels", "OfficialAccountCodes", "PersonalDataRetentionPeriods", "SAFTProductTypes", "SAFTTaxCodes", "SAFTTransactionTypes", "WTaxCategories", "WTaxRelationTypes" },
            pkg.Unavailable.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Empty(pkg.Sets.Keys.Intersect(pkg.Unavailable.Keys));
    }

    [Fact]
    public void OmitsOnlyTheColumnsAbsentFromThe2111Metadata()
    {
        var pkg = Load();
        var omitting = pkg.Sets.Where(kv => kv.Value.OmittedColumns.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value.OmittedColumns.ToArray());
        Assert.Equal(new[] { "PersonalFieldsSetups", "TaxDeclarationBoxes" }, omitting.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "SysDefined" }, omitting["PersonalFieldsSetups"]);
        Assert.Equal(new[] { "Remarks" }, omitting["TaxDeclarationBoxes"]);
    }

    [Fact]
    public void WritesDatesWithoutTheTimeSuffix()
    {
        // this build's ServiceLayer.dll has no 'T00:00:00Z' literal (it appears from 2202; absent in 2111 too): rea/01-findings.md, FP2111
        Assert.Equal("date-only", Load().DateJson);
    }
}
