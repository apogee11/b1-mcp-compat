# B1.Mcp.Compat

HTTP connector (.NET 10) that sits between SAP's **b1-mcp-server** and a Service Layer older than 10.0 FP2608 so the MCP server works unmodified. Against an FP2608+ Service Layer it is transparent.

```
MCP server ──HTTP──> B1.Mcp.Compat.Host ──HTTPS──> Service Layer
                          └── SQL (read only) ──> company database
```

## What it answers on behalf of the old Service Layer

| Request | How |
|---|---|
| `GET /b1s/v2/$metadata?scope=entityset&annotation=…` | The Service Layer's own plain `$metadata` plus annotations: **standard** objects from the embedded knowledge base; **customer-defined** ones (user fields, user tables, user objects) from the company database catalog (`CUFD`, `OUTB`, `OUDO`) |
| `POST /b1s/v2/PersonalFieldsSetupsService_GetPersonalFieldsByTable` | Rows of the company's `OPFS`, filtered like the native action (see below) |
| Anything else | Passed through untouched (cookies, bodies, status) |

If an entity set cannot be explained (a property in neither the knowledge base nor the catalog) that entity set fails alone with HTTP 502 naming the property and how to add it to `mappings.override.json`. Nothing is guessed ([ADR 0004](../docs/adr/0004-unresolved-mappings-always-fail.md)).

## Projects

| Project | Role |
|---|---|
| `B1.Mcp.Compat.Core` | Model, knowledge base, metadata synthesis, personal-data mapping. No I/O |
| `B1.Mcp.Compat.Data` | Company database access: SQL Server (Microsoft.Data.SqlClient); SAP HANA is an **experimental, untested stub** |
| `B1.Mcp.Compat.Host` | Proxy, configuration, commands |

## Commands

```
B1.Mcp.Compat.Host [serve]                  run the proxy (default)
B1.Mcp.Compat.Host gen-kb [--sl URL] [--out kb.json.gz] [--merge existing.json.gz] [--company DB]
B1.Mcp.Compat.Host version
```

`gen-kb` builds the **knowledge base** from a *native* FP2608+ Service Layer (env `B1_COMPANY_DB`, `B1_USERNAME`, `B1_PASSWORD`). It records only SAP-standard facts: label, table, column, child table and enum values of every standard entity, plus which `OPFS` rows the native personal-data action serves for each table. The company's user fields, tables and objects are excluded using that company's own catalog; the run fails if anything customer-specific slips in. Results merge with an existing file (union, newest wins). The file used at build time is `src/B1.Mcp.Compat.Host/Resources/kb.json.gz`, **embedded in the exe**; `KnowledgeBase:OverridePath` replaces it without rebuilding.

## Configuration (`appsettings.json`, section `Compat`; environment `Compat__Section__Key`)

| Key | Default | |
|---|---|---|
| `Listen` | `https://localhost:50100` | Listener. HTTPS uses a generated self-signed certificate unless `Tls` points to a PFX; `http://…` is allowed on trusted networks |
| `Upstream:Url` / `SkipTlsVerify` | `https://localhost:50000` / `true` | The Service Layer (Service Layers ship self-signed certificates) |
| `Database:Provider` | `SqlServer` | `SqlServer` or `Hana` (experimental) |
| `Database:ConnectionString` | Integrated Security | `{CompanyDB}` is replaced by the `CompanyDB` of the Login that passed through |
| `KnowledgeBase:OverridePath` | empty | Optional knowledge base file |
| `Mappings:OverridePath` | `mappings.override.json` | Manual mappings for things the connector cannot resolve |
| `SimulateLegacy` | `false` | Test only: hide the native FP2608 features of the upstream |

Direct login mode only (`POST /b1s/v2/Login` with `CompanyDB`); OAuth/Keycloak is not supported.

## Build and test

```
dotnet build compat -c Release
dotnet publish compat/src/B1.Mcp.Compat.Host -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o compat/publish
tools/parity/run-parity.ps1     # needs a native FP2608 Service Layer: native vs connector, must print N/N identical
tools/e2e/e2e.ps1 ...           # on any installation: MCP server through the connector
```

## Rules learned from real systems (see `docs/findings.es.md`)

- Customer objects come from the catalog, never from the `U_` prefix: some system columns also start with `U_`.
- `OUTB.ObjectType` 0 and 5 are the user tables exposed as `U_<TableName>`; 1 to 4 belong to user objects.
- The by-table personal-data action does not return `OPFS` rows by table name: `ORDR` returns the sales rows of `ODOC`. That mapping is learned from a native Service Layer into the knowledge base.
- The native scoped `$metadata` omits complex or collection properties that have no table mapping; the connector does the same.
