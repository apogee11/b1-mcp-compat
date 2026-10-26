using System.Text.RegularExpressions;
using B1.Mcp.Compat.Core.Catalog;
using B1.Mcp.Compat.Core.Config;
using B1.Mcp.Compat.Core.Read;
using Microsoft.Data.SqlClient;

namespace B1.Mcp.Compat.Data;

/// <summary>SQL Server access to the company database (read only: OPFS and the SELECTs of the extended read runtime).</summary>
public sealed partial class SqlServerCompanyDatabase : ICompanyDatabase
{
    private readonly DatabaseOptions _options;

    public SqlServerCompanyDatabase(DatabaseOptions options)
    {
        _options = options;
        if (options.ConnectionString != "" || options.User == "") return;
        if (options.Password == "")
            throw new InvalidOperationException("B1McpCompat:Database:User is set but there is no password: set B1McpCompat:Database:Password.");
    }

    [GeneratedRegex(@"^[A-Za-z0-9_\-\.\$#@ ]{1,128}$")]
    private static partial Regex SafeDbName();

    /// <summary>Connection string for a company database. Never logged: it may hold credentials.</summary>
    public string ConnectionStringFor(string companyDb)
    {
        if (!SafeDbName().IsMatch(companyDb)) throw new CompanyDatabaseException("Unsupported characters in company database name.");
        var template = _options.ConnectionString;
        if (template != "")
        {
            if (template.Contains("{CompanyDB}", StringComparison.OrdinalIgnoreCase))
                return template.Replace("{CompanyDB}", companyDb, StringComparison.OrdinalIgnoreCase);
            return new SqlConnectionStringBuilder(template) { InitialCatalog = companyDb }.ConnectionString;
        }
        var b = new SqlConnectionStringBuilder
        {
            DataSource = _options.Server,
            InitialCatalog = companyDb,
            TrustServerCertificate = _options.TrustServerCertificate,
            ApplicationName = "b1-mcp-compat",
        };
        if (_options.User == "") b.IntegratedSecurity = true;
        else
        {
            b.UserID = _options.User;
            b.Password = _options.Password;
        }
        return b.ConnectionString;
    }

    private async Task<SqlConnection> OpenAsync(string companyDb, CancellationToken ct)
    {
        var c = new SqlConnection(ConnectionStringFor(companyDb));
        try
        {
            await c.OpenAsync(ct);
            return c;
        }
        catch (SqlException e)
        {
            await c.DisposeAsync();
            throw new CompanyDatabaseException($"Cannot open the company database '{companyDb}' (SQL error {e.Number}): {e.Message}", e);
        }
    }

    private static async Task<List<object?[]>> QueryAsync(SqlConnection c, string sql, int cols, CancellationToken ct)
    {
        var rows = new List<object?[]>();
        try
        {
            await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 30 };
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var row = new object?[cols];
                for (var i = 0; i < cols; i++) row[i] = await r.IsDBNullAsync(i, ct) ? null : r.GetValue(i);
                rows.Add(row);
            }
        }
        catch (SqlException e)
        {
            throw new CompanyDatabaseException($"SQL query failed (error {e.Number}): {e.Message}", e);
        }
        return rows;
    }

    public async Task<IReadOnlyList<PersonalFieldRow>> LoadPersonalFieldsAsync(string companyDb, CancellationToken ct)
    {
        await using var c = await OpenAsync(companyDb, ct);
        var exists = await QueryAsync(c, "SELECT OBJECT_ID('OPFS')", 1, ct);
        if (exists.Count == 0 || exists[0][0] == null)
            throw new CompanyDatabaseException($"Table OPFS (personal data setup) does not exist in '{companyDb}'. The connector needs OPFS to serve personal data classification.");
        var rows = await QueryAsync(c,
            "SELECT AbsEntry, TableName, FieldName, CAST(RefObjType AS nvarchar(20)), CAST(Category AS nvarchar(10)), CAST(OrigType AS nvarchar(10)), CAST([Type] AS nvarchar(10)) FROM OPFS ORDER BY AbsEntry",
            7, ct);
        return rows.Select(r => new PersonalFieldRow(Convert.ToInt32(r[0]), S(r[1]), S(r[2]), r[3] as string, r[4] as string, r[5] as string, r[6] as string)).ToList();
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadUserTableColumnsAsync(string companyDb, CancellationToken ct)
    {
        await using var c = await OpenAsync(companyDb, ct);
        var rows = await QueryAsync(c,
            "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME LIKE '@%' ORDER BY TABLE_NAME, ORDINAL_POSITION", 2, ct);
        return rows.GroupBy(r => S(r[0]), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => S(r[1])).ToList(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<UserFieldRow>> LoadUserFieldsAsync(string companyDb, IReadOnlyCollection<string> tables, CancellationToken ct)
    {
        if (tables.Count == 0) return Array.Empty<UserFieldRow>();
        await using var c = await OpenAsync(companyDb, ct);
        var rows = new List<UserFieldRow>();
        try
        {
            var names = tables.Select((_, i) => "@T" + i).ToList();
            await using var cmd = new SqlCommand(
                $"SELECT TableID, FieldID, AliasID, TypeID, EditType, SizeID FROM [dbo].[CUFD] WHERE TableID IN ({string.Join(",", names)}) ORDER BY TableID, FieldID", c) { CommandTimeout = 30 };
            var i = 0;
            foreach (var t in tables) cmd.Parameters.Add(names[i++], System.Data.SqlDbType.NVarChar, 50).Value = t;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                rows.Add(new UserFieldRow(S(r.GetValue(0)), Convert.ToInt32(r.GetValue(1)), S(r.GetValue(2)), S(r.GetValue(3)),
                    r.IsDBNull(4) ? null : r.GetString(4), Convert.ToInt32(r.GetValue(5))));
        }
        catch (SqlException e)
        {
            throw new CompanyDatabaseException($"SQL query failed (error {e.Number}): {e.Message}", e);
        }
        return rows;
    }

    /// <summary>
    /// Runs one planned SELECT with its typed parameters (the declared type of each parameter is set from its declaration, e.g. "nvarchar(254)", "numeric(25,6)").
    /// Only statements the planner built reach here; a text that is not a SELECT is refused anyway.
    /// </summary>
    public async Task<IReadOnlyList<object?[]>> ReadRowsAsync(string companyDb, ReadSqlQuery query, int columns, CancellationToken ct)
    {
        if (!query.Sql.StartsWith("SELECT ", StringComparison.Ordinal)) throw new CompanyDatabaseException("Only SELECT statements are run by the read runtime.");
        await using var c = await OpenAsync(companyDb, ct);
        var rows = new List<object?[]>();
        try
        {
            await using var cmd = new SqlCommand(query.Sql, c) { CommandTimeout = 30 };
            foreach (var p in query.Params)
            {
                var sp = cmd.Parameters.Add(p.Name, p.DbType);
                var m = Declared().Match(p.Declaration);
                if (m.Groups["a"].Success)
                {
                    if (m.Groups["b"].Success) { sp.Precision = byte.Parse(m.Groups["a"].Value); sp.Scale = byte.Parse(m.Groups["b"].Value); }
                    else sp.Size = int.Parse(m.Groups["a"].Value);
                }
                sp.Value = p.Value;
            }
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var row = new object?[columns];
                for (var i = 0; i < columns; i++) row[i] = await r.IsDBNullAsync(i, ct) ? null : r.GetValue(i);
                rows.Add(row);
            }
        }
        catch (SqlException e)
        {
            throw new CompanyDatabaseException($"SQL query failed (error {e.Number}): {e.Message}", e);
        }
        return rows;
    }

    [GeneratedRegex(@"\((?<a>\d+|max)(?:,(?<b>\d+))?\)")]
    private static partial Regex Declared();

    private static string S(object? o) => (o as string ?? o?.ToString() ?? "").Trim();
}
