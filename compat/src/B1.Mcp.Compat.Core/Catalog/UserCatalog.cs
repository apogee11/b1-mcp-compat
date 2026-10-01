using System.Text.Json;

namespace B1.Mcp.Compat.Core.Catalog;

/// <summary>
/// A user table. TableType is the SAP BoUTBTableType name of UserTablesMD (bott_NoObject, bott_MasterData, bott_MasterDataLines,
/// bott_Document, bott_DocumentLines, bott_NoObjectAutoIncrement). No-object tables are exposed by the Service Layer as entity set "U_" + TableName.
/// </summary>
public sealed record UserTable(string TableName, string? Descr, string TableType)
{
    public bool IsEntitySet => TableType is "bott_NoObject" or "bott_NoObjectAutoIncrement";
}

public sealed record UserObjectChild(string TableName, string? SonName);

/// <summary>A user-defined object (UserObjectsMD) and its child tables.</summary>
public sealed record UserObject(string Code, string TableName, IReadOnlyList<UserObjectChild> Children);

/// <summary>
/// The customer-defined things of one company (user fields, user tables, user objects), read from the Service Layer's own
/// UserFieldsMD / UserTablesMD / UserObjectsMD. Table names are compared case-insensitively; user tables are keyed WITHOUT the
/// leading '@' (as in UserTablesMD.TableName), while UserFieldsMD names them WITH it.
/// </summary>
public sealed class UserCatalog
{
    /// <summary>"TABLE|U_ALIAS" to the field description (TableName as in UserFieldsMD: '@'+name for user tables).</summary>
    public Dictionary<string, string> Udfs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, UserTable> UserTables { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, UserObject> UserObjects { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static string UdfKey(string table, string column) => table + "|" + column;

    public bool TryGetUdf(string table, string column, out string descr) => Udfs.TryGetValue(UdfKey(table, column), out descr!);

    /// <summary>Does the physical user table have this column: a system column of its type, or one of its user fields.</summary>
    public bool HasColumn(string table, string column)
    {
        if (TryGetUdf(table, column, out _)) return true;
        return UserTables.TryGetValue(table.TrimStart('@'), out var t) && SystemColumns.Of(t.TableType).Contains(column);
    }

    public static UserCatalog Parse(IEnumerable<JsonElement> fields, IEnumerable<JsonElement> tables, IEnumerable<JsonElement> objects)
    {
        var c = new UserCatalog();
        foreach (var f in fields)
            c.Udfs[UdfKey(Str(f, "TableName"), "U_" + Str(f, "Name"))] = Str(f, "Description", Str(f, "Name"));
        foreach (var t in tables)
            c.UserTables[Str(t, "TableName")] = new UserTable(Str(t, "TableName"), Opt(t, "TableDescription"), Str(t, "TableType"));
        foreach (var o in objects)
        {
            var kids = new List<UserObjectChild>();
            if (o.TryGetProperty("UserObjectMD_ChildTables", out var ch) && ch.ValueKind == JsonValueKind.Array)
                kids.AddRange(ch.EnumerateArray().Select(k => new UserObjectChild(Str(k, "TableName"), Opt(k, "ObjectName"))));
            c.UserObjects[Str(o, "Code")] = new UserObject(Str(o, "Code"), Str(o, "TableName"), kids);
        }
        return c;
    }

    private static string? Opt(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Str(JsonElement e, string name, string fallback = "") => (Opt(e, name) ?? fallback).Trim();
}

/// <summary>
/// System columns every user table of a given type has (the Service Layer exposes them as properties; they annotate as themselves).
/// Verified against the plain $metadata of FP2208 and FP2408 companies with 339 and 377 user tables.
/// </summary>
public static class SystemColumns
{
    private static readonly HashSet<string> Empty = new();
    private static readonly HashSet<string> NoObject = new(StringComparer.OrdinalIgnoreCase) { "Code", "Name" };

    private static readonly HashSet<string> MasterData = new(StringComparer.OrdinalIgnoreCase)
        { "Code", "Name", "DocEntry", "Canceled", "Object", "LogInst", "UserSign", "Transfered", "CreateDate", "CreateTime", "UpdateDate", "UpdateTime", "DataSource" };

    private static readonly HashSet<string> Document = new(StringComparer.OrdinalIgnoreCase)
        { "DocEntry", "DocNum", "Period", "Instance", "Series", "Handwrtten", "Status", "RequestStatus", "Creator", "Remark", "Canceled", "Object", "LogInst",
          "UserSign", "Transfered", "CreateDate", "CreateTime", "UpdateDate", "UpdateTime", "DataSource" };

    private static readonly HashSet<string> MasterDataLines = new(StringComparer.OrdinalIgnoreCase) { "Code", "LineId", "Object", "LogInst" };
    private static readonly HashSet<string> DocumentLines = new(StringComparer.OrdinalIgnoreCase) { "DocEntry", "LineId", "Object", "LogInst", "VisOrder" };

    public static HashSet<string> Of(string tableType) => tableType switch
    {
        "bott_NoObject" or "bott_NoObjectAutoIncrement" => NoObject,
        "bott_MasterData" => MasterData,
        "bott_Document" => Document,
        "bott_MasterDataLines" => MasterDataLines,
        "bott_DocumentLines" => DocumentLines,
        _ => Empty,
    };
}

/// <summary>One row of OPFS (personal data setup) with the raw single-letter codes.</summary>
public sealed record PersonalFieldRow(int AbsEntry, string TableName, string FieldName, string? RefObjType, string? Category, string? OrigType, string? Type);

/// <summary>Raised when the company database cannot answer (unreachable, missing table, not supported).</summary>
public sealed class CompanyDatabaseException : Exception
{
    public CompanyDatabaseException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Read access to the company database, needed only for OPFS (personal data classification). SQL Server is implemented; SAP HANA is an experimental stub.</summary>
public interface ICompanyDatabase
{
    /// <summary>OPFS rows. Throws <see cref="CompanyDatabaseException"/> when OPFS does not exist.</summary>
    Task<IReadOnlyList<PersonalFieldRow>> LoadPersonalFieldsAsync(string companyDb, CancellationToken ct);
}
