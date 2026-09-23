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

    /// <summary>
    /// Connection string template. "{CompanyDB}" is replaced by the company database of the session (CompanyDB of the Login);
    /// without the placeholder the Initial Catalog is overridden. Default: Windows integrated security.
    /// </summary>
    public string ConnectionString { get; set; } = "Server=localhost;Integrated Security=true;TrustServerCertificate=true;Initial Catalog={CompanyDB}";
}
