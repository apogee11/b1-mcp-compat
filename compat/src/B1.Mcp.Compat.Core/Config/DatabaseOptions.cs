namespace B1.Mcp.Compat.Core.Config;

public enum DatabaseProvider
{
    SqlServer,

    /// <summary>EXPERIMENTAL stub: not implemented yet.</summary>
    Hana,
}

public sealed class DatabaseOptions
{
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.SqlServer;

    /// <summary>SQL Server host (and instance or port), e.g. "sqlhost", "sqlhost\SAPB1" or "sqlhost,1433".</summary>
    public string Server { get; set; } = "localhost";

    /// <summary>SQL login. Empty (default) means Windows integrated security: the account the connector runs as.</summary>
    public string User { get; set; } = "";

    /// <summary>Password of <see cref="User"/>.</summary>
    public string Password { get; set; } = "";

    public bool TrustServerCertificate { get; set; } = true;

    /// <summary>
    /// Advanced: a complete connection string template. When set it replaces Server, User and Password.
    /// "{CompanyDB}" is replaced by the company database of the session (CompanyDB of the Login);
    /// without the placeholder the Initial Catalog is overridden.
    /// </summary>
    public string ConnectionString { get; set; } = "";
}
