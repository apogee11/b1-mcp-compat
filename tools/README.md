# tools: scripts to run on other servers

Everything here is read-only against your SAP Business One installation and needs no installation on the target.

| Folder | Script | Where to run | What it does |
|---|---|---|---|
| `collect/` | `check-upstream.ps1` | any Windows machine that reaches the Service Layer | Checks that an older Service Layer gives the connector what it needs: user fields, user tables and user objects readable through the Service Layer (`UserFieldsMD`, `UserTablesMD`, `UserObjectsMD`) and matching the `U_*` properties of `$metadata`. Optional `-SqlServer` also reads `OPFS`. Output: `out\check-<Tag>\` |
| `e2e/` | `e2e.ps1` | a machine with Node.js 22+ | Runs the SAP MCP server (`b1-mcp-server/`, see the root README) through the connector (`-Exe`, or `dotnet run` from a checkout) against your Service Layer and checks its tools |
| `parity/` | `run-parity.ps1` | the development server with a native FP2608 | Proves the connector reproduces native FP2608 answers (N/N identical) |

## Quick start on an old installation (e.g. FP2208)
```powershell
cd tools\collect
$env:B1_PASSWORD = '<Service Layer password>'
.\check-upstream.ps1 -SlUrl https://SLSERVER:50000 -Company YOURCOMPANYDB -User manager -Tag 2208 -SqlServer SQLSERVER -SqlUser sa   # -SqlServer is optional
```
- Passwords: set `B1_PASSWORD` (and `B1_SQL_PASSWORD` for a SQL login) or the script prompts. Nothing is written to disk.
- Without `-SqlUser` SQL uses the Windows identity of whoever runs the script. A read-only login that can `SELECT` on `OPFS` is enough.
- The output folder holds the names of user fields, tables and objects and the `OPFS` classification (table and field names only). It contains no business rows, passwords or session ids.

Then, optionally, the end-to-end test (needs the published connector exe, the `b1-mcp-server` folder next to `tools`, Node.js 22+, and internet the first time for `npm install`):
```powershell
cd tools\e2e
.\e2e.ps1 -SlUrl https://SLSERVER:50000 -Company YOURCOMPANYDB -User manager -Exe ..\..\compat\publish\B1.Mcp.Compat.Host.exe
```
The connector reads `OPFS` from the company database with Integrated Security by default; for a SQL login add `-SqlConnection 'Server=SQLHOST;User Id=USER;Password=PWD;TrustServerCertificate=true;Initial Catalog={CompanyDB}'`.

## Notes learned the hard way
- The Service Layer rejects requests carrying `Expect: 100-continue` with a misleading "Invalid login credential"; the scripts disable it.
- .NET's cookie container drops cookies for dot-less host names (`WIN-SERVER01`), so the scripts carry the session cookie by hand.
- Old Windows defaults to TLS 1.0; the scripts force TLS 1.2.
