# Runs the connector and the MCP server from source, each in its own window, restarting on every code change.
#   .\dev.ps1                          # profile "default", both
#   .\dev.ps1 -Name cliente2208 -Only compat
#   .\dev.ps1 -SimulateLegacy          # make a native FP2608 behave as a legacy one
# Ctrl+C here stops everything. Connector: http://127.0.0.1:50300  MCP server: http://localhost:3100/mcp
param([string]$Name = 'default', [ValidateSet('both', 'compat', 'mcp')][string]$Only = 'both', [switch]$SimulateLegacy,
      [int]$ConnectorPort = 50300, [int]$McpPort = 3100)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$mcpDir = Join-Path $root 'b1-mcp-server'
if ($Only -ne 'compat' -and -not (Test-Path (Join-Path $mcpDir 'package.json'))) { throw "b1-mcp-server/ not found at the repo root. Download SAP's MCP server (or your variant) and put it there; see README, 'Get the MCP server'." }
$file = Join-Path $root ".dev\$Name.profile"
if (-not (Test-Path $file)) { throw "No profile '$Name'. Run tools\dev\setup.ps1 -Name $Name first." }
$secure = Get-Content $file | ConvertTo-SecureString
$p = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)) | ConvertFrom-Json

$sqlAuth = if ($p.SqlWindowsAuth) { 'Integrated Security=true' } else { "User Id=$($p.SqlUser);Password=$($p.SqlPassword)" }
$env:Compat__Listen = "http://127.0.0.1:$ConnectorPort"
$env:Compat__Upstream__Url = $p.SlUrl
$env:Compat__Database__ConnectionString = "Server=$($p.SqlHost);$sqlAuth;TrustServerCertificate=true;Initial Catalog={CompanyDB}"
if ($SimulateLegacy) { $env:Compat__SimulateLegacy = 'true' }

$env:NODE_ENV = 'development'; $env:AUTHENTICATION_MODE = 'direct'; $env:PORT = $McpPort; $env:HTTPS_ENABLED = 'false'
$env:SERVICE_LAYER_ROOT_URL = "http://127.0.0.1:$ConnectorPort"; $env:B1_COMPANY_DB = $p.Company; $env:B1_USERNAME = $p.User; $env:B1_PASSWORD = $p.Password
$env:AUTH_ALLOW_SELF_SIGNED = 'true'; $env:MCP_ALLOWED_HOSTS = '127.0.0.1,localhost'; $env:MCP_HUMAN_CONFIRMATION_ENABLED = 'false'; $env:APP_LOG_LEVEL = 'debug'

if ($Only -ne 'compat' -and -not (Test-Path (Join-Path $mcpDir 'dist\index.js'))) {
  Push-Location $mcpDir; try { npm install --legacy-peer-deps --no-audit --no-fund; npm run build } finally { Pop-Location }
}

$procs = @()
try {
  if ($Only -ne 'mcp') { $procs += Start-Process dotnet -ArgumentList 'watch', 'run', '--project', (Join-Path $root 'compat\src\B1.Mcp.Compat.Host') -PassThru }
  if ($Only -ne 'compat') {
    $procs += Start-Process cmd -ArgumentList '/c', 'npm run build:watch' -WorkingDirectory $mcpDir -PassThru
    $procs += Start-Process node -ArgumentList '--watch', 'dist\index.js' -WorkingDirectory $mcpDir -PassThru
  }
  Write-Host "Running profile '$Name'. Ctrl+C to stop."
  while ($procs | Where-Object { -not $_.HasExited }) { Start-Sleep 1 }
} finally {
  $procs | ForEach-Object { if (-not $_.HasExited) { taskkill /PID $_.Id /T /F | Out-Null } }
}
