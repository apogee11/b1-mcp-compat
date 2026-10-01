using System.Reflection;
using System.Text.Json;

namespace B1.Mcp.Compat.Core.Personal;

/// <summary>
/// What the native <c>GetPersonalFieldsByTable</c> asks OPFS for a requested table: <c>WHERE TableName = abstract AND Category = category</c>.
/// Documents, payments, tax invoices, opportunities, equipment cards, landed costs and payment results map to an abstract table and a
/// category (ORDR -> ODOC, 'R'); every other table is itself with category 'N'. Observed on FP2608 for all 3053 dictionary tables; the tables
/// exist unchanged in FP2208 and FP2408.
/// </summary>
public static class PersonalTableMap
{
    private static readonly Lazy<Dictionary<string, (string Table, string Category)>> Map = new(() =>
    {
        using var s = typeof(PersonalTableMap).Assembly.GetManifestResourceStream("pf-table-map.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string[]>>(s)!.ToDictionary(kv => kv.Key, kv => (kv.Value[0], kv.Value[1]), StringComparer.OrdinalIgnoreCase);
    });

    public static (string Table, string Category) Resolve(string table) => Map.Value.TryGetValue(table, out var m) ? m : (table, "N");
}
