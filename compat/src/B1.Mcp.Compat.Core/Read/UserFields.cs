using System.Globalization;

namespace B1.Mcp.Compat.Core.Read;

/// <summary>One row of CUFD (user fields of the company database). The engine reads CUFD whole, "ORDER BY TableID, FieldID" (rea/01 findings 2026-10-26).</summary>
public sealed record UserFieldRow(string TableId, int FieldId, string AliasId, string TypeId, string? EditType, int SizeId);

/// <summary>
/// CUFD rows to read columns. Rule and sources (rea/01 2026-10-26): a CUFD row applies to the table named by TableID; its column is "U_" + AliasID;
/// the order is FieldID, which equals the physical order (645/645 rows of the 2608 demo, 0 differences) and comes after the METADATA columns
/// (captured SQL of Items and Warehouses; Warehouses JSON). The SQL type follows the same rule as the METADATA columns (rea/18 2.5) and was measured
/// against the physical type of every UDF of the 2608 demo; a TypeID/EditType/SizeID that was not measured stays pending (an error when the column
/// is read), never improvised.
/// </summary>
public static class UserFields
{
    /// <summary>User field columns of a table, in FieldID order. Empty when the table has none.</summary>
    public static IReadOnlyList<ReadColumn> ColumnsOf(IEnumerable<UserFieldRow> rows, string table) =>
        rows.Where(r => r.TableId.Trim().Equals(table, StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.FieldId).Select(Column).ToList();

    public static ReadColumn Column(UserFieldRow r)
    {
        var name = "U_" + r.AliasId.Trim();
        if (!ReadPackage.IsSafeIdentifier(name)) throw new InvalidDataException($"Unsafe user field name '{name}'.");
        var edit = (r.EditType ?? "").Trim();
        var type = r.TypeId.Trim();
        string? sql = (type, edit, r.SizeId) switch
        {
            ("A", "", 1) => "char(1)",
            ("A", "", > 1) => $"nvarchar({r.SizeId.ToString(CultureInfo.InvariantCulture)})",
            ("B", "S", 16) => "numeric(19,6)",
            ("D", "", 8) => "datetime",
            ("M", "", 8) => "ntext",
            ("N", "", 11) => "int",
            _ => null,
        };
        return new ReadColumn
        {
            Name = name,
            SqlType = sql ?? "",
            Nullable = true,
            Json = new() { Property = name },
            SqlTypePending = sql == null ? $"udf-type-{type}-edit-{(edit == "" ? "none" : edit)}-size-{r.SizeId}" : null,
        };
    }
}
