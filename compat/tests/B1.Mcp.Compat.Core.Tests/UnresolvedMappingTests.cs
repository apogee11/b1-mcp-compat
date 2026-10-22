using System.Text.Json;
using Xunit;
using B1.Mcp.Compat.Core.Catalog;
using B1.Mcp.Compat.Core.Edm;
using B1.Mcp.Compat.Core.Kb;
using B1.Mcp.Compat.Core.Mappings;
using B1.Mcp.Compat.Core.Metadata;

namespace B1.Mcp.Compat.Core.Tests;

/// <summary>Unresolved cases follow the native Service Layer.</summary>
public class UnresolvedMappingTests
{
    private const string Plain = """
<edmx:Edmx xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx" Version="4.0"><edmx:DataServices>
<Schema xmlns="http://docs.oasis-open.org/odata/ns/edm" Namespace="SAPB1">
 <EnumType Name="BoColor"><Member Name="c_Red" Value="0"/><Member Name="c_Blue" Value="1"/></EnumType>
 <EntityType Name="Thing"><Key><PropertyRef Name="Code"/></Key>
  <Property Name="Code" Type="Edm.String"/><Property Name="U_Mine" Type="Edm.String"/><Property Name="U_Gone" Type="Edm.String"/><Property Name="Color" Type="SAPB1.BoColor"/>
 </EntityType>
 <EntityContainer Name="ServiceLayer"><EntitySet Name="Things" EntityType="SAPB1.Thing"/></EntityContainer>
</Schema></edmx:DataServices></edmx:Edmx>
""";

    private static MetadataSynthesizer Synth(string colorValues = "{\"c_Red\":\"R\"}")
    {
        var kb = JsonSerializer.Deserialize<KnowledgeBase>("""
{"sets":{"Things":{"type":"Thing","label":"Thing","table":"OTHG","types":{"Thing":"b1"}}},
 "blobs":{"b1":{"Code":{"l":"Code","c":"Code"},"Color":{"l":"Color","c":"Color"}}},
 "enums":{"BoColor":
""" + colorValues + "}}", KnowledgeBase.Json)!;
        return new MetadataSynthesizer(kb, MappingOverrides.Empty);
    }

    private static Task<string> Build(MetadataSynthesizer s, UserCatalog cat) =>
        s.BuildAsync(new MetadataQuery(true, true, true, true, "Things", true), EdmModel.Parse(Plain),
            new LazyCatalog(_ => Task.FromResult(cat)), null, CancellationToken.None);

    private static UserCatalog CatalogWith(params (string table, string alias)[] udfs)
    {
        var c = new UserCatalog();
        foreach (var (t, a) in udfs) c.Udfs[UserCatalog.UdfKey(t, "U_" + a)] = a + " descr";
        return c;
    }

    [Fact]
    public async Task UserFieldMissingFromTheCatalogIsOmittedLikeTheNative()
    {
        var xml = await Build(Synth(), CatalogWith(("OTHG", "Mine")));
        Assert.Contains("Name=\"U_Mine\"", xml);
        Assert.DoesNotContain("U_Gone", xml);
    }

    [Fact]
    public async Task EnumMemberWithoutStoredValueIsServedWithoutValidValue()
    {
        var xml = await Build(Synth(), CatalogWith());
        var members = System.Xml.Linq.XDocument.Parse(xml).Descendants(EdmModel.Ns + "Member").ToDictionary(m => (string)m.Attribute("Name")!);
        Assert.Contains("ValidValue", members["c_Red"].ToString());
        Assert.DoesNotContain("ValidValue", members["c_Blue"].ToString());
    }

    [Fact]
    public async Task StandardPropertyMissingFromTheKbStillFailsClosed()
    {
        var kb = Synth();
        var plain = Plain.Replace("<Property Name=\"Color\"", "<Property Name=\"Extra\" Type=\"Edm.String\"/><Property Name=\"Color\"");
        await Assert.ThrowsAsync<UnresolvedMappingException>(() => kb.BuildAsync(new MetadataQuery(true, true, true, true, "Things", true),
            EdmModel.Parse(plain), new LazyCatalog(_ => Task.FromResult(CatalogWith())), null, CancellationToken.None));
    }
}
