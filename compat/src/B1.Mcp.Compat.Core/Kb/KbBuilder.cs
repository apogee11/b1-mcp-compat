using System.Xml.Linq;
using B1.Mcp.Compat.Core.Edm;

namespace B1.Mcp.Compat.Core.Kb;

/// <summary>
/// Builds a knowledge base from the answers of a native FP2608+ Service Layer (pure: no I/O).
/// Only SAP-standard knowledge goes in: user fields (CUFD), user tables/objects and any entity set stored in an '@' table are
/// excluded and counted.
/// </summary>
public sealed class KbBuilder
{
    private readonly Func<string, string, bool> _isUserField;
    private readonly Func<string, bool> _isUserSetName;

    public KnowledgeBase Kb { get; } = new() { GeneratedAt = DateTimeOffset.UtcNow };

    public int SetsAdded { get; private set; }
    public List<string> ExcludedSets { get; } = new();
    public int ExcludedUserFields { get; private set; }

    /// <summary>Properties named U_* that were kept because they are not in CUFD (standard columns such as OUSR.U_NAME).</summary>
    public List<string> RetainedUProperties { get; } = new();

    /// <param name="isUserField">(table, property) is a user-defined field of the source company (CUFD).</param>
    /// <param name="isUserSetName">entity set name is a user table (U_ + OUTB) or user object (OUDO.Code).</param>
    public KbBuilder(Func<string, string, bool> isUserField, Func<string, bool> isUserSetName)
    {
        _isUserField = isUserField;
        _isUserSetName = isUserSetName;
    }

    /// <summary>Adds the enum facts of a scoped answer (enums are the same in every entity set).</summary>
    private void AddEnums(EdmModel scoped)
    {
        foreach (var e in scoped.Types.Values.Where(t => t.Name.LocalName == "EnumType"))
        {
            var name = (string)e.Attribute("Name")!;
            var d = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var m in e.Elements(EdmModel.Ns + "Member"))
                d[(string)m.Attribute("Name")!] = EdmModel.Annotations(m).GetValueOrDefault("SAPB1.ValidValue");
            if (!Kb.Enums.TryGetValue(name, out var cur)) Kb.Enums[name] = cur = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var (k, v) in d) cur[k] = v;
        }
    }

    /// <param name="scoped">answer to $metadata?scope=entityset&amp;annotation=labelWithField&amp;annotation=labelWithTable&amp;entityset=NAME&amp;dependency=true</param>
    /// <param name="full">the plain model of the same Service Layer (to find properties the scoped answer omits)</param>
    public bool AddSet(string name, EdmModel scoped, EdmModel full)
    {
        var setEl = scoped.SetsByName.GetValueOrDefault(name) ?? throw new InvalidDataException($"Scoped answer has no entity set {name}");
        var sa = EdmModel.Annotations(setEl);
        var table = sa.GetValueOrDefault("SAPB1.TableName");
        if (_isUserSetName(name) || (table != null && table.StartsWith('@')) || table == null)
        {
            ExcludedSets.Add(name);
            AddEnums(scoped); // enum valid values are SAP-standard even when reached through a user table/object
            return false;
        }

        var root = EdmModel.ShortType((string)setEl.Attribute("EntityType")!);
        // type -> tables, following ChildTableName from the root (same rule the connector applies at run time)
        var tables = new Dictionary<string, List<string>>(StringComparer.Ordinal) { [root] = new() { table } };
        var queue = new Queue<string>();
        queue.Enqueue(root);
        var seen = new HashSet<string>(StringComparer.Ordinal) { root };
        while (queue.Count > 0)
        {
            var tn = queue.Dequeue();
            if (!scoped.Types.TryGetValue(tn, out var t) || t.Name.LocalName == "EnumType") continue;
            foreach (var p in EdmModel.Properties(t))
            {
                var pt = EdmModel.ShortType((string?)p.Attribute("Type") ?? "");
                if (!scoped.IsStructured(pt)) continue;
                var ct = EdmModel.Annotations(p).GetValueOrDefault("SAPB1.ChildTableName");
                if (ct != null)
                {
                    if (!tables.TryGetValue(pt, out var l)) tables[pt] = l = new();
                    if (!l.Contains(ct)) l.Add(ct);
                }
                if (seen.Add(pt)) queue.Enqueue(pt);
            }
            if ((string?)t.Attribute("BaseType") is { } bt)
            {
                var b = EdmModel.ShortType(bt);
                if (!tables.ContainsKey(b)) tables[b] = new List<string>(tables[tn]);
                if (seen.Add(b)) queue.Enqueue(b);
            }
        }

        var ks = new KbSet { Type = root, Label = sa.GetValueOrDefault("Common.Label"), Table = table };
        foreach (var t in scoped.Types.Values.Where(t => t.Name.LocalName != "EnumType"))
        {
            var tn = (string)t.Attribute("Name")!;
            tables.TryGetValue(tn, out var tabs);
            var scopedProps = EdmModel.Properties(t).ToDictionary(p => (string)p.Attribute("Name")!, p => p, StringComparer.Ordinal);
            var names = new List<string>();
            if (full.Types.TryGetValue(tn, out var ft)) names.AddRange(EdmModel.Properties(ft).Select(p => (string)p.Attribute("Name")!));
            names.AddRange(scopedProps.Keys.Where(k => !names.Contains(k)));
            var props = new Dictionary<string, KbProp?>(StringComparer.Ordinal);
            foreach (var pn in names)
            {
                if (pn.StartsWith("U_", StringComparison.Ordinal))
                {
                    if (tabs != null && tabs.Any(tb => _isUserField(tb, pn))) { ExcludedUserFields++; continue; }
                    RetainedUProperties.Add($"{name}/{tn}.{pn}");
                }
                if (!scopedProps.TryGetValue(pn, out var sp)) { props[pn] = null; continue; }
                var a = EdmModel.Annotations(sp);
                if (a.Count == 0) { props[pn] = null; continue; }
                props[pn] = new KbProp { Label = a.GetValueOrDefault("Common.Label"), Column = a.GetValueOrDefault("SAPB1.ColumnName"), ChildTable = a.GetValueOrDefault("SAPB1.ChildTableName") };
            }
            ks.Types[tn] = Kb.Intern(props);
        }
        Kb.Sets[name] = ks;
        AddEnums(scoped);
        SetsAdded++;
        return true;
    }

    /// <summary>Checks that nothing company-specific slipped in; returns the offending entries.</summary>
    public List<string> FindLeaks()
    {
        var leaks = new List<string>();
        foreach (var (sn, s) in Kb.Sets)
        {
            if (s.Table != null && s.Table.StartsWith('@')) leaks.Add($"set {sn} -> {s.Table}");
            foreach (var (tn, id) in s.Types)
                foreach (var (pn, v) in Kb.Blobs[id])
                    if (pn.StartsWith("U_", StringComparison.Ordinal)
                        && !RetainedUProperties.Contains($"{sn}/{tn}.{pn}"))
                        leaks.Add($"{sn}/{tn}.{pn}");
        }
        return leaks;
    }
}
