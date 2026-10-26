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

    /// <summary>
    /// Physical columns of user tables ("@NAME" to its column names), read from the company database. Only the lines (child) tables of user
    /// objects need them: the Service Layer builds a child collection from ALL physical columns of the child table, with no list or filter
    /// (2608 engine), so columns such as EncryptIV appear there and nowhere in the Service Layer's own data.
    /// Empty when the database is not reachable (the properties then stay unresolved and the entity fails closed).
    /// </summary>
    public Dictionary<string, HashSet<string>> PhysicalColumns { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static string UdfKey(string table, string column) => table + "|" + column;

    public bool TryGetUdf(string table, string column, out string descr) => Udfs.TryGetValue(UdfKey(table, column), out descr!);

    /// <summary>Does the physical user table have this column: a system column of its type, or one of its user fields.</summary>
    public bool HasColumn(string table, string column)
    {
        if (TryGetUdf(table, column, out _)) return true;
        if (!UserTables.TryGetValue(table.TrimStart('@'), out var t)) return false;
        if (SystemColumns.Of(t.TableType).Contains(column)) return true;
        return t.TableType is "bott_MasterDataLines" or "bott_DocumentLines"
            && PhysicalColumns.TryGetValue("@" + t.TableName, out var cols) && cols.Contains(column);
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
        { "Code", "Name", "DocEntry", "Canceled", "Object", "LogInst", "UserSign", "Transfered", "CreateDate", "CreateTime", "UpdateDate", "UpdateTime", "DataSource",
          // Added by the 2608 engine to every UDO of type MasterData and Document (unconditional). Older Service Layers
          // never list them in their plain $metadata, and only properties of the plain model are ever resolved, so nothing changes there.
          "NaturalPer", "DPPStatus" };

    private static readonly HashSet<string> Document = new(StringComparer.OrdinalIgnoreCase)
        { "DocEntry", "DocNum", "Period", "Instance", "Series", "Handwrtten", "Status", "RequestStatus", "Creator", "Remark", "Canceled", "Object", "LogInst",
          "UserSign", "Transfered", "CreateDate", "CreateTime", "UpdateDate", "UpdateTime", "DataSource", "NaturalPer", "DPPStatus" };

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

/// <summary>Read access to the company database: OPFS (personal data classification) and the physical columns of user-object child tables. SQL Server is implemented; SAP HANA is an experimental stub.</summary>
public interface ICompanyDatabase
{
    /// <summary>OPFS rows. Throws <see cref="CompanyDatabaseException"/> when OPFS does not exist.</summary>
    Task<IReadOnlyList<PersonalFieldRow>> LoadPersonalFieldsAsync(string companyDb, CancellationToken ct);

    /// <summary>Physical columns of every user table ("@NAME" to its column names).</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadUserTableColumnsAsync(string companyDb, CancellationToken ct);

    /// <summary>Runs one SELECT planned by the extended read runtime and returns its rows (<paramref name="columns"/> values each, null for NULL). Read only.</summary>
    /// <summary>CUFD rows of the given tables (TableID in <paramref name="tables"/>), ordered by TableID, FieldID. Read only.</summary>
    Task<IReadOnlyList<Read.UserFieldRow>> LoadUserFieldsAsync(string companyDb, IReadOnlyCollection<string> tables, CancellationToken ct);

    Task<IReadOnlyList<object?[]>> ReadRowsAsync(string companyDb, Read.ReadSqlQuery query, int columns, CancellationToken ct);
}
