using System.Text.Json;
using Xunit;
using B1.Mcp.Compat.Core.Catalog;
using B1.Mcp.Compat.Core.Edm;
using B1.Mcp.Compat.Core.Kb;
using B1.Mcp.Compat.Core.Mappings;
using B1.Mcp.Compat.Core.Metadata;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>A data property named like its type is exposed as "&lt;Type&gt;Property" and annotated as the engine property "&lt;Type&gt;".</summary>
public class RenamedPropertyTests
{
    private const string Plain = """
<edmx:Edmx xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx" Version="4.0"><edmx:DataServices>
<Schema xmlns="http://docs.oasis-open.org/odata/ns/edm" Namespace="SAPB1">
 <EntityType Name="U_BPRSTRT"><Key><PropertyRef Name="Code"/></Key>
  <Property Name="Code" Type="Edm.String"/><Property Name="U_BPRSTRTProperty" Type="Edm.String"/>
 </EntityType>
 <EntityContainer Name="ServiceLayer"><EntitySet Name="U_BPRSTRT" EntityType="SAPB1.U_BPRSTRT"/></EntityContainer>
</Schema></edmx:DataServices></edmx:Edmx>
""";

    [Fact]
    public async Task UserFieldRenamedWithPropertySuffixIsAnnotatedWithItsPhysicalColumn()
    {
        var cat = new UserCatalog();
        cat.UserTables["BPRSTRT"] = new UserTable("BPRSTRT", "Strt", "bott_NoObject");
        cat.Udfs[UserCatalog.UdfKey("@BPRSTRT", "U_BPRSTRT")] = "Street descr";
        var kb = JsonSerializer.Deserialize<KnowledgeBase>("""{"sets":{},"blobs":{},"enums":{}}""", KnowledgeBase.Json)!;
        var xml = await new MetadataSynthesizer(kb, MappingOverrides.Empty).BuildAsync(
            new MetadataQuery(true, true, true, true, "U_BPRSTRT", true), EdmModel.Parse(Plain),
            new LazyCatalog(_ => Task.FromResult(cat)), null, CancellationToken.None);
        var p = System.Xml.Linq.XDocument.Parse(xml).Descendants(EdmModel.Ns + "Property").Single(e => (string)e.Attribute("Name")! == "U_BPRSTRTProperty");
        Assert.Contains("Street descr", p.ToString());
        Assert.Contains("U_BPRSTRT\"", p.ToString().Replace("U_BPRSTRTProperty", ""));
    }
}
