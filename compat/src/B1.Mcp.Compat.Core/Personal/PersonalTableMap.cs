using System.Text.Json;

namespace B1.Mcp.Compat.Core.Personal;

/// <summary>
/// What the native <c>GetPersonalFieldsByTable</c> asks OPFS for a requested table: <c>WHERE TableName = abstract AND Category = category</c>.
/// Documents, payments, tax invoices, opportunities, equipment cards, landed costs and payment results map to an abstract table and a
/// category (ORDR -> ODOC, 'R'); every other table is itself with category 'N'. There is one map per release (<c>pf-map-2208.json</c>, ...),
/// computed by the maintainer tooling from that release (its own OBJECT_METADATA, its own predicates and
/// <c>CorrectCategoryByRefObjType</c>, its own abstract-object constants); FP2608 matches the native action on all 3053 tables.
/// FP2208 differs: its engine does not fold tax-invoice tables into OTXI.
/// </summary>
public static class PersonalTableMap
{
    private static readonly Dictionary<string, Dictionary<string, (string Table, string Category)>> Maps = new();

    private static Dictionary<string, (string Table, string Category)> For(string release)
    {
        lock (Maps)
        {
            if (Maps.TryGetValue(release, out var m)) return m;
            using var s = typeof(PersonalTableMap).Assembly.GetManifestResourceStream($"pf-map-{release}.json")
                ?? throw new NotSupportedException($"No personal-data table map embedded for release {release}.");
            return Maps[release] = JsonSerializer.Deserialize<Dictionary<string, string[]>>(s)!
                .ToDictionary(kv => kv.Key, kv => (kv.Value[0], kv.Value[1]), StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Table and category to select from OPFS for <paramref name="table"/> in <paramref name="release"/> (e.g. "2408").</summary>
    public static (string Table, string Category) Resolve(string table, string release) => For(release).TryGetValue(table, out var m) ? m : (table, "N");
}
