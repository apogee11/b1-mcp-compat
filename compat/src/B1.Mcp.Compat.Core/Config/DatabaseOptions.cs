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

    /// <summary>Password of <see cref="User"/>. Prefer <see cref="PasswordFile"/>: an environment variable cannot change while the process runs.</summary>
    public string Password { get; set; } = "";

    /// <summary>
    /// File holding the password of <see cref="User"/>. Read on every new connection, so a rotated password
    /// is picked up without restarting the connector. Wins over <see cref="Password"/>.
    /// </summary>
    public string PasswordFile { get; set; } = "";

    public bool TrustServerCertificate { get; set; } = true;

    /// <summary>
    /// Advanced: a complete connection string template. When set it replaces Server, User, Password and PasswordFile.
    /// "{CompanyDB}" is replaced by the company database of the session (CompanyDB of the Login);
    /// without the placeholder the Initial Catalog is overridden.
    /// </summary>
    public string ConnectionString { get; set; } = "";
}
