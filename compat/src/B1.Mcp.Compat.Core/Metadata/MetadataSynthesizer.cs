using System.Text;
using System.Xml;
using System.Xml.Linq;
using B1.Mcp.Compat.Core.Catalog;
using B1.Mcp.Compat.Core.Edm;
using B1.Mcp.Compat.Core.Kb;
using B1.Mcp.Compat.Core.Mappings;

namespace B1.Mcp.Compat.Core.Metadata;

/// <summary>Lazily loads the company catalog the first time something is not covered by the knowledge base.</summary>
public sealed class LazyCatalog
{
    private readonly Func<CancellationToken, Task<UserCatalog>> _load;
    private Task<UserCatalog>? _task;

    public LazyCatalog(Func<CancellationToken, Task<UserCatalog>> load) => _load = load;

    public Task<UserCatalog> GetAsync(CancellationToken ct) => _task ??= _load(ct);
}

/// <summary>
/// Builds what a FP2608 Service Layer would answer to
/// <c>$metadata?scope=entityset&amp;annotation=labelWithTable[&amp;annotation=labelWithField&amp;entityset=X&amp;dependency=true]</c>
/// starting from the plain model of an older Service Layer.
///
/// Resolution of each property of an entity (first match wins; there is NO guessing):
///  1. mappings.override.json
///  2. knowledge base (SAP-standard facts; a null fact = natively not annotated, so the property is omitted like the native SL does)
///  3. user-defined field: (table, "U_"+alias) exists in the Service Layer's UserFieldsMD. Native format: Label = Description,
///     ColumnName = "U_"+alias (the property name), no ChildTableName.
///  4. properties of user tables (UserTablesMD no-object types, entity set "U_"+TableName, table "@"+TableName) and user objects (UserObjectsMD,
///     entity set = Code, table "@"+TableName; child tables from UserObjectMD_ChildTables): the property must be a system column of the
///     table type or one of its user fields; Label = ColumnName = property name for system columns, Label = description for U_ columns;
///     entity label = TableDescription; collection of a UDO line type: Label = TableDescription of the child table, ChildTableName = "@"+child table.
///  Renamed property: a data property named like its type is exposed as "&lt;Type&gt;Property" (OData); it resolves by its engine name "&lt;Type&gt;".
///  Unresolved cases: a user field (U_) that is not in the catalog of a known table is left out like the native SL does
///  (its engine returns a null description for a column missing in the table); an enum member without a stored value is served without ValidValue.
///  Anything else (a standard property the KB does not list, a user-table column or child the catalog does not explain, an entity
///  in neither the KB nor the catalog) still fails the entity (<see cref="UnresolvedMappingException"/>): the native behaviour is not verified for it.
/// </summary>
public sealed class MetadataSynthesizer
{
    private const string TermLabel = "Common.Label";
    private const string TermTable = "SAPB1.TableName";
    private const string TermColumn = "SAPB1.ColumnName";
    private const string TermChild = "SAPB1.ChildTableName";
    private const string TermValid = "SAPB1.ValidValue";

    private readonly KnowledgeBase _kb;
    private readonly MappingOverrides _ov;

    public MetadataSynthesizer(KnowledgeBase kb, MappingOverrides overrides)
    {
        _kb = kb;
        _ov = overrides;
    }

    private sealed record SetInfo(string? Label, string Table, UserObject? Udo, bool UserSet);

    public async Task<string> BuildAsync(MetadataQuery q, EdmModel full, LazyCatalog catalog, Action<string>? onSkipped, CancellationToken ct)
    {
        var schema = new XElement(EdmModel.Ns + "Schema", new XAttribute("Namespace", "SAPB1"));
        foreach (var (name, applies, desc) in new[]
        {
            ("TableName", "EntitySet", "Database table name"),
            ("ColumnName", "Property", "Database table column name"),
            ("ServiceCode", "EntitySet", "Business object service code"),
            ("ValidValue", "Member", "Database storage value for this enum member"),
            ("ChildTableName", "Property", "Database child table that stores this complex property either collection or single"),
        })
        {
            schema.Add(new XElement(EdmModel.Ns + "Term", new XAttribute("AppliesTo", applies), new XAttribute("Name", name), new XAttribute("Type", "Edm.String"),
                EdmModel.Annotation("Core.Description", desc)));
        }

        if (q.EntitySet == null)
        {
            var container = new XElement(EdmModel.Ns + "EntityContainer", new XAttribute("Name", "ServiceLayer"));
            foreach (var s in full.Sets)
            {
                var name = (string)s.Attribute("Name")!;
                var info = await ResolveSetAsync(name, catalog, ct);
                if (info == null) { onSkipped?.Invoke(name); continue; }
                container.Add(AnnotatedSet(s, info, q));
            }
            schema.Add(container);
        }
        else
        {
            if (!full.SetsByName.TryGetValue(q.EntitySet, out var setEl)) throw new EntitySetNotFoundException(q.EntitySet);
            var info = await ResolveSetAsync(q.EntitySet, catalog, ct)
                ?? throw new UnresolvedMappingException(q.EntitySet, new[] { new UnresolvedItem("entity", q.EntitySet, null, q.EntitySet, null) });
            await BuildScopedAsync(q, full, catalog, q.EntitySet, setEl, info, schema, ct);
        }
        return Serialize(schema);
    }

    private XElement AnnotatedSet(XElement set, SetInfo info, MetadataQuery q)
    {
        var c = Slim(set);
        if (q.Label && !string.IsNullOrEmpty(info.Label)) c.Add(EdmModel.Annotation(TermLabel, info.Label));
        if (q.WithTable) c.Add(EdmModel.Annotation(TermTable, info.Table));
        return c;
    }

    private async Task<SetInfo?> ResolveSetAsync(string name, LazyCatalog catalog, CancellationToken ct)
    {
        var o = _ov.FindEntity(name);
        if (o?.Table != null) return new SetInfo(o.Label ?? name, o.Table, null, false);
        if (_kb.Sets.TryGetValue(name, out var ks) && ks.Table != null) return new SetInfo(ks.Label, ks.Table, null, false);
        var cat = await catalog.GetAsync(ct);
        if (name.StartsWith("U_", StringComparison.Ordinal) && cat.UserTables.TryGetValue(name[2..], out var ut) && ut.IsEntitySet)
            return new SetInfo(ut.Descr, "@" + ut.TableName, null, true);
        if (cat.UserObjects.TryGetValue(name, out var udo))
        {
            cat.UserTables.TryGetValue(udo.TableName, out var mt);
            return new SetInfo(mt?.Descr, "@" + udo.TableName, udo, true);
        }
        return null;
    }

    private async Task BuildScopedAsync(MetadataQuery q, EdmModel full, LazyCatalog catalog, string setName, XElement setEl, SetInfo info, XElement schema, CancellationToken ct)
    {
        var unresolved = new List<UnresolvedItem>();
        var root = EdmModel.ShortType((string)setEl.Attribute("EntityType")!);
        var tables = new Dictionary<string, List<string>>(StringComparer.Ordinal) { [root] = new() { info.Table } };
        var seen = new HashSet<string>(StringComparer.Ordinal) { root };
        var order = new List<string>();
        var queue = new Queue<string>();
        queue.Enqueue(root);

        var typeOut = new List<XElement>();
        var enumNames = new List<string>();

        while (queue.Count > 0)
        {
            var tn = queue.Dequeue();
            if (!full.Declarations.TryGetValue(tn, out var decls)) continue;
            // Defect replicated from the Service Layer: a type name it declares twice (ComplexType "Event") is answered once per declaration.
            foreach (var tdef in decls)
            {
                if (tdef.Name.LocalName == "EnumType") { if (!enumNames.Contains(tn)) enumNames.Add(tn); continue; }
                order.Add(tn);
                var el = Slim(tdef);
                tables.TryGetValue(tn, out var tabs);
                var blob = info.UserSet ? null : _kb.TypeOf(setName, tn);

                foreach (var p in el.Elements(EdmModel.Ns + "Property").ToList())
                {
                    var pn = (string)p.Attribute("Name")!;
                    var ptype = EdmModel.ShortType((string?)p.Attribute("Type") ?? "");
                    var structured = full.IsStructured(ptype);
                    var (verdict, kp) = await DecideAsync(setName, tn, pn, ptype, structured, tabs, blob, info, catalog, ct);
                    if (verdict == Verdict.Drop)
                    {
                        // Observed natively: an omitted property still pulls its enum type into the answer (but not its structured type).
                        p.Remove();
                        if (q.Dependency && full.IsEnum(ptype) && seen.Add(ptype)) queue.Enqueue(ptype);
                        continue;
                    }
                    if (verdict == Verdict.Unresolved) { unresolved.Add(new UnresolvedItem("property", setName, tn, pn, tabs?.FirstOrDefault())); continue; }
                    if (q.Label && kp!.Label != null) p.Add(EdmModel.Annotation(TermLabel, kp.Label));
                    if (q.WithField && kp!.Column != null) p.Add(EdmModel.Annotation(TermColumn, kp.Column));
                    if (q.WithField && kp!.ChildTable != null) p.Add(EdmModel.Annotation(TermChild, kp.ChildTable));
                    if (q.Dependency && (structured || full.IsEnum(ptype)))
                    {
                        if (structured && kp!.ChildTable != null)
                        {
                            if (!tables.TryGetValue(ptype, out var l)) tables[ptype] = l = new();
                            if (!l.Contains(kp.ChildTable, StringComparer.OrdinalIgnoreCase)) l.Add(kp.ChildTable);
                        }
                        if (seen.Add(ptype)) queue.Enqueue(ptype);
                    }
                }
                if (q.Dependency && (string?)tdef.Attribute("BaseType") is { } bt)
                {
                    var b = EdmModel.ShortType(bt);
                    if (tabs != null && !tables.ContainsKey(b)) tables[b] = new List<string>(tabs);
                    if (seen.Add(b)) queue.Enqueue(b);
                }
                typeOut.Add(el);
            }
        }

        var enumOut = new List<XElement>();
        foreach (var en in enumNames)
        {
            var el = Slim(full.Types[en]);
            if (q.WithField)
            {
                _kb.Enums.TryGetValue(en, out var known);
                foreach (var m in el.Elements(EdmModel.Ns + "Member"))
                {
                    var mn = (string)m.Attribute("Name")!;
                    string? val = _ov.FindEnumValue(en, mn);
                    if (val == null)
                    {
                        if (known != null && known.TryGetValue(mn, out var kv)) val = kv;
                        // A member without a stored database value is answered without ValidValue, like the KB's null facts.
                        else continue;
                    }
                    if (val != null) m.Add(EdmModel.Annotation(TermValid, val));
                }
            }
            enumOut.Add(el);
        }

        if (unresolved.Count > 0) throw new UnresolvedMappingException(setName, unresolved);

        foreach (var e in enumOut) schema.Add(e);
        foreach (var t in typeOut.Where(t => t.Name.LocalName == "ComplexType")) schema.Add(t);
        foreach (var t in typeOut.Where(t => t.Name.LocalName == "EntityType")) schema.Add(t);
        schema.Add(new XElement(EdmModel.Ns + "EntityContainer", new XAttribute("Name", "ServiceLayer"), AnnotatedSet(setEl, info, q)));
    }

    private static readonly Dictionary<string, string[]> KeptAttributes = new()
    {
        ["EntitySet"] = new[] { "EntityType", "Name" },
        ["EntityType"] = new[] { "Name", "OpenType", "HasStream" },
        ["ComplexType"] = new[] { "Name", "OpenType" },
        ["EnumType"] = new[] { "IsFlags", "Name", "UnderlyingType" },
        ["Property"] = new[] { "Name", "Nullable", "Type" },
        ["Member"] = new[] { "Name", "Value" },
        ["PropertyRef"] = new[] { "Name" },
    };

    private static readonly HashSet<string> KeptChildren = new() { "Key", "PropertyRef", "Property", "Member" };

    /// <summary>
    /// Copy of a model element as the native scoped answer shows it: only the structural attributes and children
    /// (no NavigationProperty / NavigationPropertyBinding / concurrency annotations, no facets such as MaxLength).
    /// </summary>
    private static XElement Slim(XElement src)
    {
        var e = new XElement(src.Name);
        if (KeptAttributes.TryGetValue(src.Name.LocalName, out var keep))
            foreach (var a in src.Attributes().Where(a => keep.Contains(a.Name.LocalName))) e.Add(new XAttribute(a.Name, a.Value));
        foreach (var c in src.Elements().Where(c => KeptChildren.Contains(c.Name.LocalName))) e.Add(Slim(c));
        return e;
    }

    private enum Verdict { Annotate, Drop, Unresolved }

    private async Task<(Verdict, KbProp?)> DecideAsync(string setName, string typeName, string prop, string propType, bool structured,
        List<string>? tables, Dictionary<string, KbProp?>? blob, SetInfo info, LazyCatalog catalog, CancellationToken ct)
    {
        // 1. manual override
        foreach (var t in tables ?? new List<string> { "" })
        {
            var m = _ov.FindProperty(setName, prop, t.Length == 0 ? null : t);
            if (m != null) return (Verdict.Annotate, new KbProp { Label = m.Label ?? prop, Column = m.Column ?? prop, ChildTable = m.ChildTable });
        }

        // OData does not allow a property named like its type: the Service Layer exposes "<Type>Property" for the data property "<Type>"
        // (the engine's name lookup resolves "<Type>Property" back to "<Type>"; the annotation uses the engine property, whose
        // column is the physical one). For the user-defined paths below, look the property up by its engine name.
        var engineProp = prop.Length > "Property".Length && prop.EndsWith("Property", StringComparison.Ordinal)
            && string.Equals(prop[..^"Property".Length], typeName, StringComparison.Ordinal) ? typeName : prop;

        if (!info.UserSet)
        {
            // 2. knowledge base
            if (blob != null && blob.TryGetValue(prop, out var kp)) return kp == null ? (Verdict.Drop, null) : (Verdict.Annotate, kp);

            // A complex or collection property the dictionary does not list has no table behind it: the native $metadata leaves it out.
            if (structured && blob != null) return (Verdict.Drop, null);

            // 3. user-defined field of a standard table
            if (engineProp.StartsWith("U_", StringComparison.Ordinal) && tables != null)
            {
                var cat = await catalog.GetAsync(ct);
                foreach (var t in tables)
                    if (cat.TryGetUdf(t, engineProp, out var descr)) return (Verdict.Annotate, new KbProp { Label = descr, Column = engineProp });
                // Native rule (GetFieldConvertedDescription, slot 0x2b8): a column that does not exist in the looked-up table gives a null
                // description and the property is left out of the scoped answer, as for the KB's null facts.
                return (Verdict.Drop, null);
            }
            return (Verdict.Unresolved, null);
        }

        // 4. user table / user object
        var uc = await catalog.GetAsync(ct);
        if (structured)
        {
            var child = info.Udo?.Children.FirstOrDefault(c =>
                string.Equals(c.TableName, propType, StringComparison.OrdinalIgnoreCase) || string.Equals(c.SonName, propType, StringComparison.OrdinalIgnoreCase));
            if (child == null) return (Verdict.Unresolved, null);
            uc.UserTables.TryGetValue(child.TableName, out var ct2);
            return (Verdict.Annotate, new KbProp { Label = ct2?.Descr ?? child.TableName, ChildTable = "@" + child.TableName });
        }
        var table = tables?.FirstOrDefault();
        if (table == null || !uc.HasColumn(table, engineProp)) return (Verdict.Unresolved, null);
        if (engineProp.StartsWith("U_", StringComparison.Ordinal))
            return uc.TryGetUdf(table, engineProp, out var d) ? (Verdict.Annotate, new KbProp { Label = d, Column = engineProp }) : (Verdict.Unresolved, null);
        return (Verdict.Annotate, new KbProp { Label = engineProp, Column = engineProp });
    }

    public static readonly XNamespace Edmx = EdmModel.EdmxNs;

    private static string Serialize(XElement schema)
    {
        XElement Ref(string uri, string alias, string ns) =>
            new(Edmx + "Reference", new XAttribute("Uri", uri), new XElement(Edmx + "Include", new XAttribute("Alias", alias), new XAttribute("Namespace", ns)));
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(Edmx + "Edmx", new XAttribute("Version", "4.0"), new XAttribute(XNamespace.Xmlns + "edmx", Edmx.NamespaceName),
                Ref("https://sap.github.io/odata-vocabularies/vocabularies/Common.xml", "Common", "com.sap.vocabularies.Common.v1"),
                Ref("https://oasis-tcs.github.io/odata-vocabularies/vocabularies/Org.OData.Core.V1.xml", "Core", "Org.OData.Core.V1"),
                new XElement(Edmx + "DataServices", schema)));
        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, IndentChars = "  ", OmitXmlDeclaration = false }))
            doc.Save(w);
        // XmlWriter over a StringBuilder declares utf-16; the HTTP layer sends UTF-8.
        return sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"UTF-8\"", StringComparison.Ordinal);
    }
}
