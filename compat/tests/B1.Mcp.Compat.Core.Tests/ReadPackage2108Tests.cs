using Xunit;
using B1.Mcp.Compat.Core.Read;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>The embedded FP2108 package (B11000_2108): loads, 120 served sets, the 20 unavailable ones, its build id, the four sets with omitted columns and the date-only JSON.</summary>
public class ReadPackage2108Tests
{
    private static ReadPackage Load()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "compat", "src", "B1.Mcp.Compat.Host", "Resources", "read-B11000_2108.json.gz");
            if (File.Exists(p)) return ReadPackage.LoadGz(File.OpenRead(p));
        }
        throw new FileNotFoundException("read-B11000_2108.json.gz is not embedded in the Host resources");
    }

    [Fact]
    public void LoadsWithItsBuildId()
    {
        var pkg = Load();
        Assert.Equal("B11000_2108", pkg.Build.Package);
        Assert.Equal("10.0.170.0-a343684ff5ba-dbb1d3fec4f7-abc37fe997bd", pkg.Build.BuildId);
        Assert.Equal("1000170", pkg.Build.LoginVersion);
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
    public void OmitsOnlyTheColumnsAbsentFromThe2108Metadata()
    {
        var pkg = Load();
        var omitting = pkg.Sets.Where(kv => kv.Value.OmittedColumns.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value.OmittedColumns.ToArray());
        Assert.Equal(new[] { "ChecksForPaymentDrafts", "EBooksInvoiceTypes", "PersonalFieldsSetups", "TaxDeclarationBoxes" }, omitting.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "ECheck" }, omitting["ChecksForPaymentDrafts"]);
        Assert.Equal(new[] { "SelfPrice", "SubmitedBy" }, omitting["EBooksInvoiceTypes"].OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "SysDefined" }, omitting["PersonalFieldsSetups"]);
        Assert.Equal(new[] { "Remarks" }, omitting["TaxDeclarationBoxes"]);
    }

    [Fact]
    public void WritesDatesWithoutTheTimeSuffix()
    {
        // this build's ServiceLayer.dll has no 'T00:00:00Z' literal (it appears from 2202): rea/01-findings.md, FP2108
        Assert.Equal("date-only", Load().DateJson);
    }
}
