# tools: scripts to run on other servers

Everything here is read-only against your SAP Business One installation and needs no installation on the target.

| Folder | Script | Where to run | What it does |
|---|---|---|---|
| `collect/` | `run-all.ps1` | any Windows machine that reaches the Service Layer and the SQL server (PowerShell 4+, nothing to install) | Runs the three probes below and produces one zip |
| | `01-service-layer.ps1` | idem | Login, `$metadata` (full and the FP2608 scoped variants), personal-data endpoints, `$apply`, `$count`, paging. Only reads document numbers |
| | `02-database.ps1` | idem | Catalog of `OPFS`, `CUFD`, `OUTB`, `OUDO` (names only). SQL Server; HANA is not covered by this probe |
| | `03-cross-check.ps1` | offline | Compares what the Service Layer exposes with the database catalog |
| `e2e/` | `e2e.ps1` | a machine with Node.js 22+ | Runs the SAP MCP server (`b1-mcp-server/`, see the root README) through the connector (`-Exe`, or `dotnet run` from a checkout) against your Service Layer and checks its tools |
| `parity/` | `run-parity.ps1` | the development server with a native FP2608 | Proves the connector reproduces native FP2608 answers (N/N identical) |

## Quick start on an old installation (e.g. FP2208)
```powershell
cd tools\collect
.\run-all.ps1 -SlUrl https://SLSERVER:50000 -Company YOURCOMPANYDB -User manager -SqlServer SQLSERVER
```
- Passwords: set `B1_PASSWORD` (and `B1_SQL_PASSWORD` for a SQL login) or the script prompts. Nothing is written to disk.
- SQL uses the Windows identity of whoever runs the script. A read-only login that can `SELECT` on `CUFD`, `OPFS`, `OUTB`, `OUDO` and `INFORMATION_SCHEMA` is enough.
- Send back the zip printed at the end. It contains entity and field **names** (including the customer's user-defined ones) and feature checks. It contains no business rows, passwords or session ids.

Then, optionally, the end-to-end test (needs the published connector exe, the `b1-mcp-server` folder next to `tools`, Node.js 22+, and internet the first time for `npm install`):
```powershell
cd tools\e2e
.\e2e.ps1 -SlUrl https://SLSERVER:50000 -Company YOURCOMPANYDB -User manager -Exe ..\..\compat\publish\B1.Mcp.Compat.Host.exe
```
The connector reads the company database with Integrated Security by default; for a SQL login add `-SqlConnection 'Server=SQLHOST;User Id=USER;Password=PWD;TrustServerCertificate=true;Initial Catalog={CompanyDB}'`.

## Notes learned the hard way
- The Service Layer rejects requests carrying `Expect: 100-continue` with a misleading "Invalid login credential"; the scripts disable it.
- .NET's cookie container drops cookies for dot-less host names (`WIN-SERVER01`), so the scripts carry the session cookie by hand.
- Old Windows defaults to TLS 1.0; the scripts force TLS 1.2.
