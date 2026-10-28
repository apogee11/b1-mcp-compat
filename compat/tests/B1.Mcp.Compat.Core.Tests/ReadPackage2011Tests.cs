using Xunit;
using B1.Mcp.Compat.Core.Read;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>The embedded FP2011 package (B11000_2011): loads, 116 served sets, the 24 unavailable ones, its build id, the four sets with omitted columns and the date-only JSON.</summary>
public class ReadPackage2011Tests
{
    private static ReadPackage Load()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "compat", "src", "B1.Mcp.Compat.Host", "Resources", "read-B11000_2011.json.gz");
            if (File.Exists(p)) return ReadPackage.LoadGz(File.OpenRead(p));
        }
        throw new FileNotFoundException("read-B11000_2011.json.gz is not embedded in the Host resources");
    }

    [Fact]
    public void LoadsWithItsBuildId()
    {
        var pkg = Load();
        Assert.Equal("B11000_2011", pkg.Build.Package);
        Assert.Equal("10.0.140.0-8d3df26fae3e-529fef948965-e1cbf097ef30", pkg.Build.BuildId);
        Assert.Equal("1000140", pkg.Build.LoginVersion);
    }

    [Fact]
    public void Serves116SetsAndLists24Unavailable()
    {
        var pkg = Load();
        Assert.Equal(116, pkg.Sets.Count);
        Assert.Equal(new[] { "ATDocumentTypes", "ApprovalAuthorizorReplacements", "BlockExecutables", "CAEAInformations", "DatevRuns", "DatevTransactionsDeliveriesAndServices", "DeductibleTaxes", "EBooksFeesCategories", "EBooksOtherTaxCategories", "EBooksRecordTypes", "EBooksStampDutyCategories", "ElsterRun", "ISDDocuments", "ImportToExcels", "LegalTextFormats", "LegalTextItems", "OfficialAccountCodes", "PersonalDataRetentionPeriods", "SAFTProductTypes", "SAFTTaxCodes", "SAFTTransactionTypes", "SpecificWTHAmountsService", "WTaxCategories", "WTaxRelationTypes" },
            pkg.Unavailable.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Empty(pkg.Sets.Keys.Intersect(pkg.Unavailable.Keys));
    }

    [Fact]
    public void OmitsOnlyTheColumnsAbsentFromThe2011Metadata()
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
    public void SpecificWTHAmountsIsUnavailableBecauseItsKeyColumnIsMissing()
    {
        var u = Load().Unavailable["SpecificWTHAmountsService"];
        Assert.Equal("key-column-missing", u.Reason);
        Assert.Equal("OSWA", u.Table);
        Assert.Equal(new[] { "CUSplit", "SmRnNtWTx" }, u.MissingColumns.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void WritesDatesWithoutTheTimeSuffix()
    {
        // this build's ServiceLayer.dll has no 'T00:00:00Z' literal (it appears from 2202): rea/01-findings.md, FP2011
        Assert.Equal("date-only", Load().DateJson);
    }
}
