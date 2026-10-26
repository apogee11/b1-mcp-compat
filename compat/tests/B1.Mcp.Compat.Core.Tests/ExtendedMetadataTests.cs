using System.Text;
using System.Xml.Linq;
using System.Text.Json;
using Xunit;
using B1.Mcp.Compat.Core.Catalog;
using B1.Mcp.Compat.Core.Edm;
using B1.Mcp.Compat.Core.Kb;
using B1.Mcp.Compat.Core.Mappings;
using B1.Mcp.Compat.Core.Metadata;
using B1.Mcp.Compat.Core.Read;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>The $metadata of the extended read sets: plain model with the sets the Service Layer lacks, and the scoped/annotated answer built from the package.</summary>
public class ExtendedMetadataTests
{
    private const string PackageJson = """
    {
      "schemaVersion": 2,
      "build": { "package": "TEST_1", "buildId": "1.0.0.0-aaaaaaaaaaaa", "loginVersion": "1000001" },
      "enums": {},
      "edm": {
        "types": {
          "Thing": { "kind": "entity", "key": ["Code"], "pullsEnums": ["HiddenEnum"], "properties": [
            { "name": "Code", "type": "Edm.String", "nullable": false, "label": "Code", "column": "Code" },
            { "name": "Kind", "type": "SAPB1.KindEnum", "label": "Kind", "column": "KindCol" },
            { "name": "ThingLines", "type": "Collection(SAPB1.ThingLine)", "label": "Lines", "childTable": "THG1" } ] },
          "ThingLine": { "kind": "complex", "properties": [
            { "name": "LineNum", "type": "Edm.Int32", "nullable": false, "label": "Line", "column": "LineNum" } ] }
        },
        "enums": {
          "KindEnum": [ { "name": "k_A", "value": 0, "validValue": "A" }, { "name": "k_B", "value": 1 } ],
          "HiddenEnum": [ { "name": "h_X", "value": 0, "validValue": "X" } ]
        }
      },
      "sets": {
        "Things": {
          "entityType": "Thing", "label": "Things label", "table": "OTHG", "primaryKey": ["Code"], "aliases": ["Thingies"],
          "columns": [ { "name": "Code", "sqlType": "nvarchar(20)", "nullable": false, "json": { "property": "Code", "edmType": "Edm.String" } },
                       { "name": "KindCol", "sqlType": "char(1)", "nullable": true, "json": { "property": "Kind", "edmType": "SAPB1.KindEnum", "enum": "KindEnum" } } ],
          "propertyOrder": ["Code", "Kind"], "children": [
            { "collection": "ThingLines", "entityType": "ThingLine", "table": "THG1", "join": [ { "childColumn": "Code", "parentColumn": "Code" } ], "orderBy": ["Code"],
              "propertyOrder": ["LineNum"], "columns": [ { "name": "Code", "sqlType": "nvarchar(20)", "nullable": false, "json": null }, { "name": "LineNum", "sqlType": "int", "nullable": false, "json": { "property": "LineNum" } } ] } ],
          "rules": []
        }
      },
      "unavailable": {}
    }
    """;

    private const string Plain = """
    <edmx:Edmx xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx" Version="4.0"><edmx:DataServices>
    <Schema xmlns="http://docs.oasis-open.org/odata/ns/edm" Namespace="SAPB1">
     <EnumType IsFlags="false" Name="BoColor" UnderlyingType="Edm.Int32"><Member Name="c_Red" Value="0"/></EnumType>
     <EntityType Name="Order"><Key><PropertyRef Name="Id"/></Key><Property Name="Id" Type="Edm.Int32" Nullable="false"/></EntityType>
     <EntityContainer Name="ServiceLayer"><EntitySet Name="Orders" EntityType="SAPB1.Order"/></EntityContainer>
    </Schema></edmx:DataServices></edmx:Edmx>
    """;

    /// <summary>A Service Layer that lists Thingies with its own type Thing (like a 2408 that has DeductibleTaxs).</summary>
    private static readonly string PlainWithThingies = Plain
        .Replace("<EntitySet Name=\"Orders\"", "<EntitySet Name=\"Thingies\" EntityType=\"SAPB1.Thing\"/><EntitySet Name=\"Orders\"")
        .Replace("<EntityType Name=\"Order\">", "<EntityType Name=\"Thing\"><Key><PropertyRef Name=\"Code\"/></Key><Property Name=\"Code\" Type=\"Edm.String\" Nullable=\"false\"/></EntityType><EntityType Name=\"Order\">");

    private static ReadPackage Pkg() => ReadPackage.Load(new MemoryStream(Encoding.UTF8.GetBytes(PackageJson)));

    private static MetadataSynthesizer Synth(string kbJson = "{\"sets\":{},\"blobs\":{},\"enums\":{}}") =>
        new(JsonSerializer.Deserialize<KnowledgeBase>(kbJson, KnowledgeBase.Json)!, MappingOverrides.Empty);

    private static Task<string> Scoped(MetadataSynthesizer s, string set, EdmModel plain, ExtendedMetadata ext, bool dependency = true) =>
        s.BuildAsync(new MetadataQuery(true, true, true, true, set, dependency), plain,
            new LazyCatalog(_ => Task.FromResult(new UserCatalog())), null, CancellationToken.None, ext);

    [Fact]
    public void PlainModelGainsTheSetsAndAliasesTheSlLacks()
    {
        var plain = EdmModel.Parse(Plain);
        var ext = ExtendedMetadata.Build(Pkg(), plain)!;
        Assert.Equal(new[] { "Things", "Thingies" }, ext.Announced.Keys);
        Assert.Equal("SAPB1.Thing", (string)ext.Model.SetsByName["Thingies"].Attribute("EntityType")!);
        Assert.True(ext.Model.Types.ContainsKey("ThingLine") && ext.Model.IsEnum("KindEnum") && ext.Model.IsEnum("HiddenEnum"));
        Assert.True(ext.Model.SetsByName.ContainsKey("Orders"));
        Assert.False(plain.SetsByName.ContainsKey("Things"));   // the plain model is untouched
        var xml = XDocument.Parse(ext.ToXml());
        Assert.Equal("UTF-8", xml.Declaration?.Encoding);
        Assert.Single(xml.Descendants(EdmModel.Ns + "EntityType"), e => (string)e.Attribute("Name")! == "Order");
    }

    [Fact]
    public void ASetNameTheSlAlreadyListsIsNotAnnouncedAndItsTypeStaysTheSls()
    {
        var ext = ExtendedMetadata.Build(Pkg(), EdmModel.Parse(PlainWithThingies))!;
        Assert.Equal(new[] { "Things" }, ext.Announced.Keys);
        Assert.Equal("Thingies", ext.NativeAliasOf("Things"));
        Assert.False(ext.IsInjected("Thing"));
        Assert.Single(ext.Model.Declarations["Thing"]);
    }

    [Fact]
    public void NothingToAnnounceOrNoEdmMeansNoExtension()
    {
        var all = Plain.Replace("<EntitySet Name=\"Orders\"", "<EntitySet Name=\"Things\" EntityType=\"SAPB1.Order\"/><EntitySet Name=\"Thingies\" EntityType=\"SAPB1.Order\"/><EntitySet Name=\"Orders\"");
        Assert.Null(ExtendedMetadata.Build(Pkg(), EdmModel.Parse(all)));
        var v1 = ReadPackage.Load(new MemoryStream(Encoding.UTF8.GetBytes(ReadTestData.PackageJson)));
        Assert.Null(ExtendedMetadata.Build(v1, EdmModel.Parse(Plain)));
    }

    [Fact]
    public async Task ScopedAnswerCarriesTheAnnotationsOfThePackage()
    {
        var plain = EdmModel.Parse(Plain);
        var ext = ExtendedMetadata.Build(Pkg(), plain)!;
        var xml = XDocument.Parse(await Scoped(Synth(), "Things", plain, ext));
        string[] Names(string kind) => xml.Descendants(EdmModel.Ns + kind).Select(e => (string)e.Attribute("Name")!).ToArray();
        Assert.Equal(new[] { "Things" }, Names("EntitySet"));
        Assert.Equal(new[] { "Thing" }, Names("EntityType"));
        Assert.Equal(new[] { "ThingLine" }, Names("ComplexType"));
        Assert.Equal(new[] { "KindEnum", "HiddenEnum" }, Names("EnumType"));   // HiddenEnum: pulled by an omitted property
        var anns = xml.Descendants(EdmModel.Ns + "Annotation").Select(a => ((string)a.Attribute("Term")!, (string)a.Attribute("String")!)).ToList();
        Assert.Contains(("Common.Label", "Things label"), anns);
        Assert.Contains(("SAPB1.TableName", "OTHG"), anns);
        Assert.Contains(("SAPB1.ColumnName", "KindCol"), anns);
        Assert.Contains(("SAPB1.ChildTableName", "THG1"), anns);
        Assert.Contains(("SAPB1.ValidValue", "A"), anns);   // k_B has none: no annotation, member kept
        Assert.Equal(2, xml.Descendants(EdmModel.Ns + "Member").Count(m => (string)m.Attribute("Name")! is "k_A" or "k_B"));
        var noDep = XDocument.Parse(await Scoped(Synth(), "Things", plain, ext, dependency: false));
        Assert.Empty(noDep.Descendants(EdmModel.Ns + "ComplexType"));
    }

    [Fact]
    public async Task ASetWithTheSlsOwnTypeUsesTheFactsOfTheSetTheSlLists()
    {
        var plain = EdmModel.Parse(PlainWithThingies);
        var ext = ExtendedMetadata.Build(Pkg(), plain)!;
        var s = Synth("{\"sets\":{\"Thingies\":{\"type\":\"Thing\",\"label\":\"Native label\",\"table\":\"OTHG\",\"types\":{\"Thing\":\"b1\"}}},\"blobs\":{\"b1\":{\"Code\":{\"l\":\"Code\",\"c\":\"Code\"}}},\"enums\":{}}");
        var xml = await Scoped(s, "Things", plain, ext);
        Assert.Contains("Name=\"Things\"", xml);
        Assert.Contains("Native label", xml);
    }

    private static string? RealPackagePath()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "compat", "src", "B1.Mcp.Compat.Host", "Resources", "read-B11000SP_2408.json.gz");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    [Fact]
    public async Task The131ServedSetsOf2408SpAreAnnouncedAndEachAnswersItsScopedMetadata()
    {
        var path = RealPackagePath();
        if (path == null) return;   // the embedded package is not in this tree
        var pkg = ReadPackage.LoadGz(File.OpenRead(path));
        var plain = EdmModel.Parse(Plain);
        var ext = ExtendedMetadata.Build(pkg, plain)!;
        Assert.Equal(131 + 1, ext.Announced.Count);   // 131 sets + the DeductibleTaxs alias
        Assert.True(ext.Announced.ContainsKey("DeductibleTaxes") && ext.Announced.ContainsKey("DeductibleTaxs"));
        Assert.All(pkg.Unavailable.Keys, n => Assert.False(ext.Model.SetsByName.ContainsKey(n)));
        // the model is closed: every announced set type and every property type is declared
        foreach (var s in ext.Model.Sets.Where(s => (string)s.Attribute("Name")! != "Orders"))
            Assert.True(ext.Model.Types.ContainsKey(EdmModel.ShortType((string)s.Attribute("EntityType")!)));
        foreach (var t in ext.Model.Types.Values.Where(t => t.Name.LocalName != "EnumType"))
            foreach (var p in EdmModel.Properties(t))
            {
                var st = EdmModel.ShortType((string)p.Attribute("Type")!);
                Assert.True(st.StartsWith("Edm.") || ext.Model.Types.ContainsKey(st), st);
            }
        foreach (var name in pkg.Sets.Keys)
        {
            var xml = await Scoped(Synth(), name, plain, ext);
            Assert.Contains($"Name=\"{name}\"", xml);
            XDocument.Parse(xml);
        }
        Assert.Contains(XDocument.Parse(ext.ToXml()).Descendants(EdmModel.Ns + "EntitySet"), e => (string)e.Attribute("Name")! == "DeductibleTaxes");
    }
}
