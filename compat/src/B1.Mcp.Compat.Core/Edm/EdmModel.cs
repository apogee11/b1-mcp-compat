using System.Xml.Linq;

namespace B1.Mcp.Compat.Core.Edm;

/// <summary>Parsed, indexed OData CSDL document (the Service Layer $metadata).</summary>
public sealed class EdmModel
{
    public static readonly XNamespace Ns = "http://docs.oasis-open.org/odata/ns/edm";
    public static readonly XNamespace EdmxNs = "http://docs.oasis-open.org/odata/ns/edmx";

    public XDocument Document { get; }

    /// <summary>EntityType, ComplexType and EnumType elements by short name.</summary>
    public IReadOnlyDictionary<string, XElement> Types { get; }

    /// <summary>
    /// Every declaration of each type name, in document order. The Service Layer declares some names twice
    /// (FP2608 declares ComplexType "Event" twice, once for webhooks and once for event categories) and a scoped $metadata
    /// repeats every declaration of a referenced type.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<XElement>> Declarations { get; }

    /// <summary>EntitySets in document order.</summary>
    public IReadOnlyList<XElement> Sets { get; }

    public IReadOnlyDictionary<string, XElement> SetsByName { get; }

    private EdmModel(XDocument doc)
    {
        Document = doc;
        var types = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var decls = new Dictionary<string, List<XElement>>(StringComparer.Ordinal);
        foreach (var e in doc.Descendants())
        {
            if (e.Name.Namespace != Ns) continue;
            var ln = e.Name.LocalName;
            if ((ln is "EntityType" or "ComplexType" or "EnumType") && e.Attribute("Name") is { } n)
            {
                types[n.Value] = e;
                if (!decls.TryGetValue(n.Value, out var l)) decls[n.Value] = l = new();
                l.Add(e);
            }
        }
        Types = types;
        Declarations = decls.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<XElement>)kv.Value, StringComparer.Ordinal);
        Sets = doc.Descendants(Ns + "EntityContainer").SelectMany(c => c.Elements(Ns + "EntitySet")).ToList();
        SetsByName = Sets.GroupBy(s => (string)s.Attribute("Name")!).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    public static EdmModel Parse(string xml) => new(XDocument.Parse(xml));

    public static EdmModel FromDocument(XDocument doc) => new(doc);

    /// <summary>Strips Collection(...) and the SAPB1. namespace from a property type.</summary>
    public static string ShortType(string type)
    {
        if (type.StartsWith("Collection(", StringComparison.Ordinal) && type.EndsWith(')')) type = type[11..^1];
        if (type.StartsWith("Edm.", StringComparison.Ordinal)) return type;
        var i = type.LastIndexOf('.');
        return i >= 0 ? type[(i + 1)..] : type;
    }

    public bool IsEnum(string shortType) => Types.TryGetValue(shortType, out var t) && t.Name.LocalName == "EnumType";

    public bool IsStructured(string shortType) => Types.TryGetValue(shortType, out var t) && t.Name.LocalName != "EnumType";

    public static IEnumerable<XElement> Properties(XElement type) => type.Elements(Ns + "Property");

    /// <summary>Term to value of the direct Annotation children.</summary>
    public static Dictionary<string, string> Annotations(XElement e) =>
        e.Elements(Ns + "Annotation").Where(a => a.Attribute("Term") != null)
         .GroupBy(a => (string)a.Attribute("Term")!)
         .ToDictionary(g => g.Key, g => (string?)g.First().Attribute("String") ?? "", StringComparer.Ordinal);

    public static XElement Annotation(string term, string value) =>
        new(Ns + "Annotation", new XAttribute("String", value), new XAttribute("Term", term));
}
