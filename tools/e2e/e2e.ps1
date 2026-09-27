# End-to-end check on a customer installation: vendored SAP MCP server -> b1-mcp-compat -> your Service Layer (+ company database).
# Needs Node.js 22+ (and internet the first time for `npm install`) on the machine that runs it; the SL and SQL can be elsewhere.
# The connector is the self-contained exe (-Exe) or, from a source checkout, `dotnet run`.
#   .\e2e.ps1 -SlUrl https://server:50000 -Company SBODEMO -User manager -Exe ..\..\compat\publish\B1.Mcp.Compat.Host.exe
# Database: Integrated Security by default. For a SQL login pass -SqlConnection 'Server=HOST;User Id=..;Password=..;TrustServerCertificate=true;Initial Catalog={CompanyDB}'
param(
  [Parameter(Mandatory)][string]$SlUrl, [Parameter(Mandatory)][string]$Company, [Parameter(Mandatory)][string]$User,
  [string]$Exe = '', [string]$SqlConnection = '', [int]$ConnectorPort = 50300, [int]$McpPort = 3100, [string]$OutDir = '.\out\e2e'
)
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$mcpDir = Join-Path $root 'b1-mcp-server'
$OutDir = New-OutDir $OutDir
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { Write-Result FAIL 'Node.js is not installed or not in PATH (22+ required).'; exit 2 }
if (-not (Test-Path (Join-Path $mcpDir 'dist\index.js'))) {
  Write-Result INFO 'Building the vendored MCP server (first run)...'
  Push-Location $mcpDir; try { npm install --legacy-peer-deps --no-audit --no-fund | Out-Null; npm run build | Out-Null } finally { Pop-Location }
}
$pw = Get-PlainPassword 'B1_PASSWORD' "Service Layer password for $User"
$procs = @()
try {
  $env:Compat__Listen = "http://127.0.0.1:$ConnectorPort"; $env:Compat__Upstream__Url = $SlUrl
  if ($SqlConnection) { $env:Compat__Database__ConnectionString = $SqlConnection }
  if ($Exe) { $procs += Start-Process $Exe -WorkingDirectory (Split-Path (Resolve-Path $Exe)) -RedirectStandardOutput "$OutDir\connector.log" -RedirectStandardError "$OutDir\connector.err.log" -WindowStyle Hidden -PassThru }
  else { $procs += Start-Process dotnet -ArgumentList @('run', '-c', 'Release', '--project', (Join-Path $root 'compat\src\B1.Mcp.Compat.Host')) -RedirectStandardOutput "$OutDir\connector.log" -RedirectStandardError "$OutDir\connector.err.log" -WindowStyle Hidden -PassThru }

  $env:NODE_ENV = 'development'; $env:AUTHENTICATION_MODE = 'direct'; $env:PORT = $McpPort; $env:HTTPS_ENABLED = 'false'
  $env:SERVICE_LAYER_ROOT_URL = "http://127.0.0.1:$ConnectorPort"; $env:B1_COMPANY_DB = $Company; $env:B1_USERNAME = $User; $env:B1_PASSWORD = $pw
  $env:AUTH_ALLOW_SELF_SIGNED = 'true'; $env:MCP_ALLOWED_HOSTS = '127.0.0.1,localhost'; $env:MCP_HUMAN_CONFIRMATION_ENABLED = 'false'; $env:APP_LOG_LEVEL = 'info'
  $procs += Start-Process node -ArgumentList 'dist\index.js' -WorkingDirectory $mcpDir -RedirectStandardOutput "$OutDir\mcp.log" -RedirectStandardError "$OutDir\mcp.err.log" -WindowStyle Hidden -PassThru

  $up = $false
  for ($i = 0; $i -lt 40 -and -not $up; $i++) { Start-Sleep 1; try { $up = (Invoke-Http GET "http://localhost:$McpPort/health").Status -eq 200 } catch {} }
  if (-not $up) { Write-Result FAIL "MCP server did not start. See $OutDir\mcp.err.log and connector.err.log"; exit 2 }
  Write-Result INFO "MCP server up on :$McpPort via connector on :$ConnectorPort"
  & node (Join-Path $PSScriptRoot 'e2e-client.mjs') "http://localhost:$McpPort/mcp" "$OutDir\e2e-report.json"
  $code = $LASTEXITCODE
} finally {
  $procs | ForEach-Object { try { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue } catch {} }
  foreach ($n in 'B1_PASSWORD', 'B1_USERNAME', 'B1_COMPANY_DB', 'SERVICE_LAYER_ROOT_URL', 'Compat__Listen', 'Compat__Upstream__Url', 'Compat__Database__ConnectionString') { Remove-Item "Env:$n" -ErrorAction SilentlyContinue }
}
Write-Host "`nLogs and report: $OutDir  (connector.log shows what was synthesized)"
exit $code
