# Test an installation whose services are already running (connector + MCP server, yours or SAP's). Starts nothing, needs no credentials:
# the MCP server already holds them. Only needs Node.js 22+ and the MCP SDK (from b1-mcp-server/node_modules if present; otherwise run `npm install @modelcontextprotocol/sdk` in this folder).
#   .\check-installed.ps1                                  # MCP server at http://localhost:3000/mcp
#   .\check-installed.ps1 -McpUrl http://host:3000/mcp
# Default is the quick check (handshake, tools, a few schemas, one row of Currencies). -Full also asks for the schema of every entity set (~500 calls):
# the MCP server's default rate limit (120 per minute) rejects that, so raise MCP_RATE_LIMIT_MAX on it first.
param([string]$McpUrl = 'http://localhost:3000/mcp', [switch]$Full, [string]$OutDir = '.\out\check-installed')
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')
$ErrorActionPreference = 'Stop'
$OutDir = New-OutDir $OutDir
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { Write-Result FAIL 'Node.js is not installed or not in PATH (22+ required).'; exit 2 }
$health = $McpUrl -replace '/mcp/?$', '/health'
try { $up = (Invoke-Http GET $health -TimeoutSec 10).Status -eq 200 } catch { $up = $false }
if (-not $up) { Write-Result FAIL "No MCP server answering at $health. Start it (README step 5) or pass -McpUrl."; exit 2 }
Write-Result INFO "MCP server up at $McpUrl"
$clientArgs = @((Join-Path $PSScriptRoot 'e2e-client.mjs'), $McpUrl, "$OutDir\report.json")
if (-not $Full) { $clientArgs += '--quick' }
& node @clientArgs
exit $LASTEXITCODE
