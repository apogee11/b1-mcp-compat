using System.Text;
using System.Xml;
using System.Xml.Linq;
using B1.Mcp.Compat.Core.Edm;
using B1.Mcp.Compat.Core.Read;

namespace B1.Mcp.Compat.Core.Metadata;

/// <summary>
/// The $metadata of the extended read sets (rea/19, schemaVersion 2 of the read package): the Service Layer's plain model of the company plus the entity sets
/// of the package that this Service Layer does not expose, under the exact name each one is asked for (a set and its alias are judged on their own names).
/// Types come from the package (<see cref="ReadEdm"/>, the native 2608 capture restricted to what the build serves); a type name the Service Layer already declares
/// is left as the Service Layer has it (its own declaration wins; <see cref="NativeAliasOf"/> says which of its own sets it is).
/// What the package does not carry is not invented: no NavigationProperty / binding, no UDF, no facets (pending <c>edm-navigation</c>).
/// </summary>
public sealed class ExtendedMetadata
{
    private readonly Dictionary<string, ReadSet> _announced;
    private readonly Dictionary<string, string> _nativeAlias;
    private readonly HashSet<string> _types;
    private readonly HashSet<string> _enums;
    private readonly ReadEdm _edm;

    /// <summary>The plain model plus the injected sets, types and enums (a copy: the plain model is untouched).</summary>
    public EdmModel Model { get; }

    /// <summary>Entity sets added to the model (name to package set).</summary>
    public IReadOnlyDictionary<string, ReadSet> Announced => _announced;

    private ExtendedMetadata(ReadEdm edm, EdmModel model, Dictionary<string, ReadSet> announced, Dictionary<string, string> nativeAlias, HashSet<string> types, HashSet<string> enums)
    {
        _edm = edm;
        Model = model;
        _announced = announced;
        _nativeAlias = nativeAlias;
        _types = types;
        _enums = enums;
    }

    /// <summary>Null when the package has no EDM (schemaVersion 1) or nothing in it is missing from the Service Layer.</summary>
    public static ExtendedMetadata? Build(ReadPackage package, EdmModel plain)
    {
        var edm = package.Edm;
        if (edm == null) return null;
        var doc = new XDocument(plain.Document);
        var schema = doc.Descendants(EdmModel.Ns + "Schema").FirstOrDefault();
        var container = doc.Descendants(EdmModel.Ns + "EntityContainer").FirstOrDefault();
        if (schema == null || container == null) return null;

        // each name of each set (the set's own and its aliases) the Service Layer does not list
        var announced = new Dictionary<string, ReadSet>(StringComparer.Ordinal);
        foreach (var (key, set) in package.Sets)
            foreach (var n in new[] { key }.Concat(set.Aliases).Distinct(StringComparer.Ordinal))
                if (!plain.SetsByName.ContainsKey(n)) announced.TryAdd(n, set);
        if (announced.Count == 0) return null;

        // closure of the types the announced sets need (a type the Service Layer already declares is neither touched nor followed)
        var types = new HashSet<string>(StringComparer.Ordinal);
        var enums = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(announced.Values.SelectMany(s => new[] { s.EntityType }.Concat(s.Children.Select(c => c.EntityType))));
        while (queue.Count > 0)
        {
            var tn = queue.Dequeue();
            if (plain.Types.ContainsKey(tn) || types.Contains(tn) || !edm.Types.TryGetValue(tn, out var t)) continue;
            types.Add(tn);
            foreach (var p in t.Properties)
            {
                var st = ShortType(p.Type);
                if (edm.Enums.ContainsKey(st)) { if (!plain.Types.ContainsKey(st)) enums.Add(st); }
                else if (edm.Types.ContainsKey(st)) queue.Enqueue(st);
            }
            foreach (var en in t.PullsEnums ?? new()) if (!plain.Types.ContainsKey(en)) enums.Add(en);
        }

        // the native flat $metadata lists the enums in ordinal name order (measured on the 2608: 530 of 530; the engine keeps them in a std::map), so each goes to its place
        foreach (var e in edm.Enums.Where(e => enums.Contains(e.Key)).OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            var el = EnumElement(e.Key, e.Value);
            var next = schema.Elements(EdmModel.Ns + "EnumType").FirstOrDefault(x => string.CompareOrdinal((string?)x.Attribute("Name"), e.Key) > 0);
            if (next != null) next.AddBeforeSelf(el); else InsertAfterLast(schema, "EnumType", new[] { el });
        }
        InsertAfterLast(schema, "ComplexType", edm.Types.Where(t => t.Value.Kind == "complex" && types.Contains(t.Key)).OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => TypeElement(t.Key, t.Value)));
        InsertAfterLast(schema, "EntityType", edm.Types.Where(t => t.Value.Kind == "entity" && types.Contains(t.Key)).OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => TypeElement(t.Key, t.Value)));
        var nativeAlias = new Dictionary<string, string>(StringComparer.Ordinal);
        // convention (decision closed 2026-10-27): every injected set, complex and entity type goes to the end of its section in ordinal name order
        foreach (var (name, set) in announced.OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            container.Add(new XElement(EdmModel.Ns + "EntitySet", new XAttribute("EntityType", "SAPB1." + set.EntityType), new XAttribute("Name", name)));
            // the set's type is the Service Layer's own: the only facts there are come from the set it already lists under another name
            if (plain.Types.ContainsKey(set.EntityType) && package.NamesOf(name).FirstOrDefault(plain.SetsByName.ContainsKey) is { } native) nativeAlias[name] = native;
        }
        return new ExtendedMetadata(edm, EdmModel.FromDocument(doc), announced, nativeAlias, types, enums);
    }

    /// <summary>True when the type was added by this extension (its properties and annotations come from the package).</summary>
    public bool IsInjected(string type) => _types.Contains(type);

    /// <summary>True when the enum was added by this extension (its members and ValidValue come from the package).</summary>
    public bool IsInjectedEnum(string name) => _enums.Contains(name);

    /// <summary>The name under which the Service Layer lists the same set when the announced one uses the Service Layer's own type (DeductibleTaxes to DeductibleTaxs); null otherwise.</summary>
    public string? NativeAliasOf(string announcedName) => _nativeAlias.GetValueOrDefault(announcedName);

    /// <summary>Annotation facts of a property of an injected type (what the native scoped answer would carry).</summary>
    public Kb.KbProp? PropertyOf(string type, string property)
    {
        var p = _edm.Types.GetValueOrDefault(type)?.Properties.FirstOrDefault(x => x.Name == property);
        return p == null ? null : new Kb.KbProp { Label = p.Label, Column = p.Column, ChildTable = p.ChildTable };
    }

    /// <summary>Enums the native scoped answer pulls in (dependency=true) because of properties it leaves out.</summary>
    public IReadOnlyList<string> PulledEnumsOf(string type) => (_edm.Types.GetValueOrDefault(type)?.PullsEnums ?? new()).ToList();

    /// <summary>ValidValue (database storage value) of a member of an injected enum.</summary>
    public string? ValidValueOf(string enumName, string member) =>
        _edm.Enums.GetValueOrDefault(enumName)?.FirstOrDefault(m => m.Name == member)?.ValidValue;

    /// <summary>The $metadata document as the Service Layer would send it, serialized as UTF-8 XML.</summary>
    public string ToXml()
    {
        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, IndentChars = "  ", OmitXmlDeclaration = false }))
            Model.Document.Save(w);
        return sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"UTF-8\"", StringComparison.Ordinal);
    }

    private static string ShortType(string t)
    {
        if (t.StartsWith("Collection(", StringComparison.Ordinal) && t.EndsWith(')')) t = t[11..^1];
        return t.StartsWith("SAPB1.", StringComparison.Ordinal) ? t[6..] : t;
    }

    private static void InsertAfterLast(XElement schema, string kind, IEnumerable<XElement> add)
    {
        var list = add.ToList();
        if (list.Count == 0) return;
        var anchor = schema.Elements(EdmModel.Ns + kind).LastOrDefault() ?? schema.Elements().LastOrDefault(e => e.Name.LocalName == "Term") ?? schema.Elements().FirstOrDefault();
        if (anchor == null) schema.Add(list);
        else anchor.AddAfterSelf(list);
    }

    private static XElement EnumElement(string name, List<ReadEdmMember> members) =>
        new(EdmModel.Ns + "EnumType", new XAttribute("IsFlags", "false"), new XAttribute("Name", name), new XAttribute("UnderlyingType", "Edm.Int32"),
            members.Select(m => new XElement(EdmModel.Ns + "Member", new XAttribute("Name", m.Name), new XAttribute("Value", m.Value))));

    private static XElement TypeElement(string name, ReadEdmType t)
    {
        var e = new XElement(EdmModel.Ns + (t.Kind == "entity" ? "EntityType" : "ComplexType"), new XAttribute("Name", name));
        if (t.OpenType) e.Add(new XAttribute("OpenType", "true"));
        if (t.HasStream) e.Add(new XAttribute("HasStream", "true"));
        if (t.Kind == "entity") e.Add(new XElement(EdmModel.Ns + "Key", t.Key!.Select(k => new XElement(EdmModel.Ns + "PropertyRef", new XAttribute("Name", k)))));
        foreach (var p in t.Properties)
        {
            var pe = new XElement(EdmModel.Ns + "Property", new XAttribute("Name", p.Name));
            if (p.Nullable == false) pe.Add(new XAttribute("Nullable", "false"));
            pe.Add(new XAttribute("Type", p.Type));
            e.Add(pe);
        }
        return e;
    }
}
