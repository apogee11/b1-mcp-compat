# Parity test: native FP2608 vs b1-mcp-compat (.NET) with SimulateLegacy=true, both seen through the unmodified SAP MCP server.
#
#   native    : MCP(:51001) -> Service Layer (native FP2608)
#   candidate : MCP(:51002) -> compat(:51010, SimulateLegacy) -> Service Layer (native FP2608)
#
# Needs: Node 22, the built b1-mcp-server (dist\index.js), the SL reachable, SQL Server reachable for the compat connector.
# Credentials are read from the MCP's .env (B1_COMPANY_DB / B1_USERNAME / B1_PASSWORD) or from the environment; never printed.
#   .\run-parity.ps1                      # uses the published exe if present, else 'dotnet run'
#   .\run-parity.ps1 -Exe .\x.exe -SlUrl https://localhost:50000 -EnvFile C:\path\to\.env
param(
  [string]$SlUrl = 'https://localhost:50000',
  [string]$EnvFile = 'C:\Users\Administrator\Desktop\b1-mcp-server\b1-mcp-server\.env',
  [string]$Exe = '',
  [int]$NativeMcpPort = 51001, [int]$CandidateMcpPort = 51002, [int]$CompatPort = 51010,
  [string]$OutDir = (Join-Path $env:TEMP 'b1-mcp-compat-parity'),
  [switch]$Verbose
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$mcpDir = Join-Path $root 'b1-mcp-server'
$compatDir = Join-Path $root 'compat'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

# credentials: environment first, then the .env file (values are not printed)
foreach ($n in 'B1_COMPANY_DB', 'B1_USERNAME', 'B1_PASSWORD') {
  if (-not [Environment]::GetEnvironmentVariable($n) -and (Test-Path $EnvFile)) {
    $line = Get-Content $EnvFile | Where-Object { $_ -match "^\s*$n\s*=" } | Select-Object -First 1
    if ($line) { [Environment]::SetEnvironmentVariable($n, ($line -replace "^\s*$n\s*=\s*", '').Trim().Trim('"').Trim("'")) }
  }
  if (-not [Environment]::GetEnvironmentVariable($n)) { throw "Missing $n (environment or $EnvFile)" }
}
if (-not (Test-Path (Join-Path $mcpDir 'dist\index.js'))) { throw "b1-mcp-server is not built: run 'npm install; npm run build' in $mcpDir" }

if (-not $Exe) {
  $cand = Get-ChildItem -Path (Join-Path $compatDir 'src\B1.Mcp.Compat.Host\bin\Release') -Recurse -Filter 'B1.Mcp.Compat.Host.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\obj\\' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if ($cand) { $Exe = $cand.FullName }
}
$procs = @()
function Wait-Http([string]$Url, [int]$Seconds = 40) {
  for ($i = 0; $i -lt $Seconds; $i++) { try { $r = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 3; if ($r.StatusCode -lt 500) { return $true } } catch { if ($_.Exception.Response) { return $true } }; Start-Sleep 1 }
  return $false
}
try {
  # 1. the .NET connector (HTTP on loopback, simulating a legacy Service Layer)
  $env:Compat__Listen = "http://127.0.0.1:$CompatPort"; $env:Compat__SimulateLegacy = 'true'; $env:Compat__Upstream__Url = $SlUrl; $env:Compat__Release = '2608'; $env:Compat__Build = '10.0.340.0-d02489c511c7-7e3e810900f1-fe64a1a2b194'
  if ($Exe) { $p = Start-Process -FilePath $Exe -WorkingDirectory (Split-Path $Exe) -RedirectStandardOutput "$OutDir\compat.log" -RedirectStandardError "$OutDir\compat.err.log" -WindowStyle Hidden -PassThru }
  else { $p = Start-Process -FilePath dotnet -ArgumentList @('run', '-c', 'Release', '--project', (Join-Path $compatDir 'src\B1.Mcp.Compat.Host')) -RedirectStandardOutput "$OutDir\compat.log" -RedirectStandardError "$OutDir\compat.err.log" -WindowStyle Hidden -PassThru }
  $procs += $p
  Remove-Item Env:Compat__Listen, Env:Compat__SimulateLegacy, Env:Compat__Upstream__Url, Env:Compat__Release, Env:Compat__Build -ErrorAction SilentlyContinue

  # 2. two MCP servers
  $mcpEnv = @{ NODE_ENV = 'development'; AUTHENTICATION_MODE = 'direct'; HTTPS_ENABLED = 'false'; AUTH_ALLOW_SELF_SIGNED = 'true';
               MCP_ALLOWED_HOSTS = '127.0.0.1,localhost'; MCP_HUMAN_CONFIRMATION_ENABLED = 'false'; APP_LOG_LEVEL = 'info' }
  foreach ($k in $mcpEnv.Keys) { Set-Item "Env:$k" $mcpEnv[$k] }
  $env:PORT = $NativeMcpPort; $env:SERVICE_LAYER_ROOT_URL = $SlUrl
  $procs += Start-Process node -ArgumentList 'dist\index.js' -WorkingDirectory $mcpDir -RedirectStandardOutput "$OutDir\mcp-native.log" -RedirectStandardError "$OutDir\mcp-native.err.log" -WindowStyle Hidden -PassThru
  $env:PORT = $CandidateMcpPort; $env:SERVICE_LAYER_ROOT_URL = "http://127.0.0.1:$CompatPort"
  $procs += Start-Process node -ArgumentList 'dist\index.js' -WorkingDirectory $mcpDir -RedirectStandardOutput "$OutDir\mcp-candidate.log" -RedirectStandardError "$OutDir\mcp-candidate.err.log" -WindowStyle Hidden -PassThru

  if (-not (Wait-Http "http://127.0.0.1:$CompatPort/")) { throw "compat connector did not start. See $OutDir\compat.err.log" }
  foreach ($port in $NativeMcpPort, $CandidateMcpPort) { if (-not (Wait-Http "http://127.0.0.1:$port/health")) { throw "MCP on :$port did not start. See $OutDir\mcp-*.err.log" } }
  Write-Host "native :$NativeMcpPort -> $SlUrl ; candidate :$CandidateMcpPort -> compat :$CompatPort -> $SlUrl"

  $a = @((Join-Path $PSScriptRoot 'compare.mjs'), '--native', "http://127.0.0.1:$NativeMcpPort/mcp", '--candidate', "http://127.0.0.1:$CandidateMcpPort/mcp")
  if ($Verbose) { $a += '--verbose' }
  & node @a
  $code = $LASTEXITCODE
} finally {
  $procs | ForEach-Object { try { & taskkill /T /F /PID $_.Id 2>&1 | Out-Null } catch {} }
  foreach ($n in 'B1_PASSWORD', 'B1_USERNAME', 'B1_COMPANY_DB', 'SERVICE_LAYER_ROOT_URL', 'PORT') { Remove-Item "Env:$n" -ErrorAction SilentlyContinue }
}
Write-Host "Logs: $OutDir (compat.log shows which answers were synthesized)"
exit $code
