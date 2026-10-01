using System.Text.RegularExpressions;
using B1.Mcp.Compat.Core.Catalog;
using B1.Mcp.Compat.Core.Config;
using Microsoft.Data.SqlClient;

namespace B1.Mcp.Compat.Data;

/// <summary>SQL Server access to the company database (read only: OPFS).</summary>
public sealed partial class SqlServerCompanyDatabase : ICompanyDatabase
{
    private readonly DatabaseOptions _options;

    public SqlServerCompanyDatabase(DatabaseOptions options)
    {
        _options = options;
        if (options.ConnectionString != "" || options.User == "") return;
        if (options.PasswordFile == "" && options.Password == "")
            throw new InvalidOperationException("Compat:Database:User is set but there is no password: set Compat:Database:PasswordFile (preferred) or Compat:Database:Password.");
        if (options.PasswordFile != "" && !File.Exists(options.PasswordFile))
            throw new InvalidOperationException($"Compat:Database:PasswordFile '{options.PasswordFile}' does not exist.");
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
            b.Password = _options.PasswordFile != "" ? File.ReadAllText(_options.PasswordFile).TrimEnd('\r', '\n') : _options.Password;
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

    private static string S(object? o) => (o as string ?? o?.ToString() ?? "").Trim();
}
