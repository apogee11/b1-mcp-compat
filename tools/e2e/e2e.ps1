# End-to-end check on a customer installation: SAP MCP server (b1-mcp-server/) -> b1-mcp-compat -> your Service Layer (+ company database).
# Needs Node.js 22+ and an already built b1-mcp-server (npm install + npm run build, done by you) on the machine that runs it; the SL and SQL can be elsewhere.
# The connector is the self-contained exe (-Exe) or, from a source checkout, `dotnet run`.
#   .\e2e.ps1 -SlUrl https://server:50000 -Company SBODEMO -User manager -Exe ..\..\compat\publish\B1.Mcp.Compat.Host.exe
# For an installation whose services are already running (the README's step 6), use check-installed.ps1 instead.
# -Release / -Build: release (2608) and build id printed by tools\collect\check-build.ps1; the Service Layer cannot tell them. -AllowUnsupportedBuild: use the release's knowledge base for a build without one (not verified).
# -SkipTlsVerify: the connector accepts the Service Layer's certificate without validating it (self-signed certificate).
# Database: Integrated Security by default. For a SQL login pass -SqlConnection 'Server=HOST;User Id=..;Password=..;TrustServerCertificate=true;Initial Catalog={CompanyDB}'
param(
  [Parameter(Mandatory)][string]$SlUrl, [Parameter(Mandatory)][string]$Company, [Parameter(Mandatory)][string]$User,
  [string]$Exe = '', [string]$SqlConnection = '', [int]$ConnectorPort = 50300, [string]$Release = '', [string]$Build = '', [switch]$AllowUnsupportedBuild, [switch]$SkipTlsVerify, [int]$McpPort = 3100, [string]$OutDir = '.\out\e2e'
)
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$mcpDir = Join-Path $root 'b1-mcp-server'
$OutDir = New-OutDir $OutDir
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { Write-Result FAIL 'Node.js is not installed or not in PATH (22+ required).'; exit 2 }
if (-not (Test-Path (Join-Path $mcpDir 'package.json'))) { Write-Result FAIL "b1-mcp-server/ not found at the repo root. Download SAP's MCP server (or your variant) and put it there; see README, 'Get the MCP server'."; exit 2 }
if (-not (Test-Path (Join-Path $mcpDir 'dist\index.js'))) {
  Write-Result FAIL "b1-mcp-server is not built. Run 'npm install --legacy-peer-deps; npm run build' in $mcpDir first, then run this script again."; exit 2
}
$hadPassword = [bool]$env:B1_PASSWORD   # only clean up what this script set
$pw = Get-PlainPassword 'B1_PASSWORD' "Service Layer password for $User"
$procs = @()
try {
  $env:B1McpCompat__Listen = "http://127.0.0.1:$ConnectorPort"; $env:B1McpCompat__ServiceLayer__Url = $SlUrl
  if ($Release) { $env:B1McpCompat__Release = $Release }; if ($Build) { $env:B1McpCompat__Build = $Build }; if ($AllowUnsupportedBuild) { $env:B1McpCompat__AllowUnsupportedBuild = 'true' }
  if ($SkipTlsVerify) { $env:B1McpCompat__ServiceLayer__SkipTlsVerify = 'true' }
  if ($SqlConnection) { $env:B1McpCompat__Database__ConnectionString = $SqlConnection }
  if ($Exe) { $procs += Start-Process $Exe -WorkingDirectory (Split-Path (Resolve-Path $Exe)) -RedirectStandardOutput "$OutDir\connector.log" -RedirectStandardError "$OutDir\connector.err.log" -WindowStyle Hidden -PassThru }
  else { $procs += Start-Process dotnet -ArgumentList @('run', '-c', 'Release', '--project', (Join-Path $root 'compat\src\B1.Mcp.Compat.Host')) -RedirectStandardOutput "$OutDir\connector.log" -RedirectStandardError "$OutDir\connector.err.log" -WindowStyle Hidden -PassThru }

  # The MCP server asks for the schema at start-up: the connector must already accept connections or it crashes with ECONNREFUSED.
  Write-Result INFO "Waiting for the connector on :$ConnectorPort (up to 120 s)..."
  $listening = $false
  for ($i = 0; $i -lt 120 -and -not $listening; $i++) {
    if ($procs[0].HasExited) { break }
    $tcp = New-Object System.Net.Sockets.TcpClient
    try { $tcp.Connect('127.0.0.1', $ConnectorPort); $listening = $true } catch { Start-Sleep 1 } finally { $tcp.Close() }
  }
  if (-not $listening) { Write-Result FAIL "The connector did not start listening on :$ConnectorPort (exited or timed out). See $OutDir\connector.err.log"; exit 2 }

  $env:NODE_ENV = 'development'; $env:AUTHENTICATION_MODE = 'direct'; $env:PORT = $McpPort; $env:HTTPS_ENABLED = 'false'
  $env:SERVICE_LAYER_ROOT_URL = "http://127.0.0.1:$ConnectorPort"; $env:B1_COMPANY_DB = $Company; $env:B1_USERNAME = $User; $env:B1_PASSWORD = $pw
  $env:MCP_RATE_LIMIT_MAX = '1000000'; # the schema sweep makes ~500 calls; the server default is 120 per minute
  $env:AUTH_ALLOW_SELF_SIGNED = 'true'; $env:MCP_ALLOWED_HOSTS = '127.0.0.1,localhost'; $env:MCP_HUMAN_CONFIRMATION_ENABLED = 'false'; $env:APP_LOG_LEVEL = 'info'
  $procs += Start-Process node -ArgumentList 'dist\index.js' -WorkingDirectory $mcpDir -RedirectStandardOutput "$OutDir\mcp.log" -RedirectStandardError "$OutDir\mcp.err.log" -WindowStyle Hidden -PassThru

  Write-Result INFO "Waiting for the MCP server (up to 120 s; the first schema load is slow)..."
  $up = $false
  for ($i = 0; $i -lt 120 -and -not $up; $i++) { Start-Sleep 1; try { $up = (Invoke-Http GET "http://localhost:$McpPort/health").Status -eq 200 } catch {} }
  if (-not $up) { Write-Result FAIL "MCP server did not start. See $OutDir\mcp.err.log and connector.err.log"; exit 2 }
  Write-Result INFO "MCP server up on :$McpPort via connector on :$ConnectorPort"
  & node (Join-Path $PSScriptRoot 'e2e-client.mjs') "http://localhost:$McpPort/mcp" "$OutDir\e2e-report.json"
  $code = $LASTEXITCODE
} finally {
  $procs | ForEach-Object { try { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue } catch {} }
  if (-not $hadPassword) { Remove-Item Env:B1_PASSWORD -ErrorAction SilentlyContinue }
  foreach ($n in 'MCP_RATE_LIMIT_MAX', 'B1_USERNAME', 'B1_COMPANY_DB', 'SERVICE_LAYER_ROOT_URL', 'B1McpCompat__Listen', 'B1McpCompat__ServiceLayer__Url', 'B1McpCompat__Database__ConnectionString', 'B1McpCompat__Release', 'B1McpCompat__Build', 'B1McpCompat__AllowUnsupportedBuild', 'B1McpCompat__ServiceLayer__SkipTlsVerify') { Remove-Item "Env:$n" -ErrorAction SilentlyContinue }
}
Write-Host "`nLogs and report: $OutDir  (connector.log shows what was synthesized)"
exit $code
