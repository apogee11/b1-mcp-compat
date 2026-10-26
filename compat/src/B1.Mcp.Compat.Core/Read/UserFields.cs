using System.Globalization;

namespace B1.Mcp.Compat.Core.Read;

/// <summary>
/// One row of CUFD (user fields of the company database; the engine reads CUFD whole, "ORDER BY TableID, FieldID", rea/01 findings 2026-10-26)
/// plus the physical type of its column in the table (<see cref="PhysicalType"/>, from sys.columns, e.g. "nvarchar(100)", "numeric(19,6)", "datetime";
/// null when the table has no such column).
/// </summary>
public sealed record UserFieldRow(string TableId, int FieldId, string AliasId, string TypeId, string? EditType, int SizeId, string? PhysicalType = null);

/// <summary>
/// CUFD rows to read columns. Rules and sources (rea/01 2026-10-26 and 2026-10-27):
/// - a CUFD row applies to the table named by TableID (a user table keeps its "@" prefix); its column is "U_" + AliasID; the order is FieldID (= physical order);
///   they come after the METADATA columns.
/// - The SQL type to read is the physical type of the column (sys.columns), not a rule on CUFD: the DDL builder was not needed.
/// - The JSON is decided by the engine's own type letter. <c>DBMTableColumn::SetUserColumnDefinition</c> copies CUFD.TypeID to column +0x30 and the first
///   character of CUFD.EditType to +0x6c, with no mapping. <c>CServiceData::GetPropertyValue</c> (ServiceLayer.dll) then reads by that letter, where 'N' with
///   edit 'T' becomes 'T': A and M text, B double, D date, I and N integer, T "HH:MM:00". Any other letter is an engine error (here: pending).
///   User fields are properties without enum ("NoEnumName") and valid values (UFD1) are not read when a value is written. Nothing about the EditType changes
///   the letter except N+T, so B with P, R, Q, % or S is always a double, and D with T is a date.
/// A combination whose letter and physical type do not agree stays pending (an error when the column is read), never improvised.
/// </summary>
public static class UserFields
{
    private static readonly HashSet<string> Text = new(StringComparer.OrdinalIgnoreCase) { "char", "nchar", "varchar", "nvarchar", "ntext", "text" };

    /// <summary>User field columns of a table, in FieldID order. Empty when the table has none.</summary>
    public static IReadOnlyList<ReadColumn> ColumnsOf(IEnumerable<UserFieldRow> rows, string table) =>
        rows.Where(r => r.TableId.Trim().Equals(table, StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.FieldId).Select(Column).ToList();

    public static ReadColumn Column(UserFieldRow r)
    {
        var name = "U_" + r.AliasId.Trim();
        if (!ReadPackage.IsSafeIdentifier(name)) throw new InvalidDataException($"Unsafe user field name '{name}'.");
        var edit = (r.EditType ?? "").Trim();
        var type = r.TypeId.Trim();
        var phys = (r.PhysicalType ?? "").Trim();
        var pbase = phys.Contains('(') ? phys[..phys.IndexOf('(')] : phys;
        // engine type letter (GetPropertyValue) vs the physical type that the value is read from
        var edm = (string?)null;
        var ok = pbase.Length != 0 && type switch
        {
            "A" or "M" => Text.Contains(pbase),
            "B" => pbase is "numeric" or "decimal",
            "N" when edit == "T" => pbase is "int" or "smallint" && (edm = "Edm.TimeOfDay") != null,
            "N" => pbase is "int" or "smallint",
            "D" => pbase == "datetime",
            _ => false,
        };
        return new ReadColumn
        {
            Name = name,
            SqlType = ok ? phys : "",
            Nullable = true,
            Json = new() { Property = name, EdmType = ok ? edm : null },
            SqlTypePending = ok ? null : $"udf-type-{type}-edit-{(edit == "" ? "none" : edit)}-size-{r.SizeId.ToString(CultureInfo.InvariantCulture)}-physical-{(phys == "" ? "missing" : phys)}",
        };
    }
}
