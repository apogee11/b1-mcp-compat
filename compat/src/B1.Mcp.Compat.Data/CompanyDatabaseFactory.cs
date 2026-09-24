using B1.Mcp.Compat.Core.Catalog;
using B1.Mcp.Compat.Core.Config;

namespace B1.Mcp.Compat.Data;

public static class CompanyDatabaseFactory
{
    public static ICompanyDatabase Create(DatabaseOptions options) => options.Provider switch
    {
        DatabaseProvider.SqlServer => new SqlServerCompanyDatabase(options),
        DatabaseProvider.Hana => new HanaCompanyDatabase(),
        _ => throw new NotSupportedException($"Unknown database provider {options.Provider}"),
    };
}
