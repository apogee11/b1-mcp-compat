namespace B1.Mcp.Compat.Core.Catalog;

/// <summary>
/// A user table (OUTB). ObjectType (SAP BoUTBTableType): 0 bott_NoObject, 1 bott_MasterData, 2 bott_MasterDataLines, 3 bott_Document,
/// 4 bott_DocumentLines, 5 bott_NoObjectAutoIncrement. Types 0 and 5 are exposed by the Service Layer as entity set "U_" + TableName.
/// </summary>
public sealed record UserTable(string TableName, string? Descr, int ObjectType)
{
    public bool IsEntitySet => ObjectType is 0 or 5;
}

public sealed record UserObjectChild(string TableName, string? SonName);

/// <summary>A user-defined object (OUDO) and its child tables (UDO1).</summary>
public sealed record UserObject(string Code, string TableName, IReadOnlyList<UserObjectChild> Children);

/// <summary>One row of OPFS (personal data setup) with the raw single-letter codes.</summary>
public sealed record PersonalFieldRow(int AbsEntry, string TableName, string FieldName, string? RefObjType, string? Category, string? OrigType, string? Type);

/// <summary>
/// Read-only snapshot of the company database catalog that is needed to explain company-specific things.
/// Table names are compared case-insensitively. User tables are keyed WITHOUT the leading '@' (as in OUTB.TableName);
/// the physical table is '@' + name.
/// </summary>
public sealed class CompanyCatalog
{
    /// <summary>"TABLE|U_ALIAS" to CUFD.Descr (TableID, '@'+TableName for user tables).</summary>
    public Dictionary<string, string> Udfs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, UserTable> UserTables { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, UserObject> UserObjects { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Physical columns of user tables, keyed by '@Name'.</summary>
    public Dictionary<string, HashSet<string>> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void AddUdf(string table, string alias, string? descr) => Udfs[UdfKey(table, "U_" + alias)] = descr ?? alias;

    public static string UdfKey(string table, string column) => table + "|" + column;

    public bool TryGetUdf(string table, string column, out string descr) => Udfs.TryGetValue(UdfKey(table, column), out descr!);

    public bool HasColumn(string table, string column) => Columns.TryGetValue(table, out var c) && c.Contains(column);
}

/// <summary>Raised when the company database cannot answer (unreachable, missing table, not supported).</summary>
public sealed class CompanyDatabaseException : Exception
{
    public CompanyDatabaseException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Access to the company database. SQL Server is implemented; SAP HANA is an experimental stub.</summary>
public interface ICompanyDatabase
{
    /// <summary>CUFD, OUTB, OUDO/UDO1 and the columns of user tables.</summary>
    Task<CompanyCatalog> LoadCatalogAsync(string companyDb, CancellationToken ct);

    /// <summary>OPFS rows. Throws <see cref="CompanyDatabaseException"/> when OPFS does not exist.</summary>
    Task<IReadOnlyList<PersonalFieldRow>> LoadPersonalFieldsAsync(string companyDb, CancellationToken ct);
}
