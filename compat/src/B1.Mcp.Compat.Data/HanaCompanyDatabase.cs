using B1.Mcp.Compat.Core.Catalog;

namespace B1.Mcp.Compat.Data;

/// <summary>
/// EXPERIMENTAL STUB. SAP HANA support is not implemented yet. The interface and the provider switch exist so that it can be
/// added: implement <see cref="ICompanyDatabase"/> with the same query as <see cref="SqlServerCompanyDatabase"/>
/// (OPFS) against the company schema.
/// </summary>
public sealed class HanaCompanyDatabase : ICompanyDatabase
{
    private const string Msg = "EXPERIMENTAL: the SAP HANA database provider is not implemented yet. Use Database:Provider = SqlServer.";

    public Task<IReadOnlyList<PersonalFieldRow>> LoadPersonalFieldsAsync(string companyDb, CancellationToken ct) => throw new NotSupportedException(Msg);

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadUserTableColumnsAsync(string companyDb, CancellationToken ct) => throw new NotSupportedException(Msg);
}
