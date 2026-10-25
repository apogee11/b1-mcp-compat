# Runs the connector from source, restarting on every code change.
#   .\dev.ps1                          # profile "default"
#   .\dev.ps1 -Name cliente2208
#   .\dev.ps1 -SimulateLegacy          # make a native FP2608 behave as a legacy one
# Ctrl+C here stops it. Connector: http://127.0.0.1:50300. The MCP server is started separately (README, 'Run from the console').
param([string]$Name = 'default', [switch]$SimulateLegacy, [string]$Release, [string]$Build, [switch]$AllowUnsupportedBuild, [int]$ConnectorPort = 50300)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$file = Join-Path $root ".dev\$Name.profile"
if (-not (Test-Path $file)) { throw "No profile '$Name'. Run tools\dev\set-credentials.ps1 -Name $Name first." }
$secure = Get-Content $file | ConvertTo-SecureString
$p = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)) | ConvertFrom-Json

$sqlAuth = if ($p.SqlWindowsAuth) { 'Integrated Security=true' } else { "User Id=$($p.SqlUser);Password=$($p.SqlPassword)" }
$env:B1McpCompat__Listen = "http://127.0.0.1:$ConnectorPort"
$env:B1McpCompat__ServiceLayer__Url = $p.SlUrl
$env:B1McpCompat__Database__ConnectionString = "Server=$($p.SqlHost);$sqlAuth;TrustServerCertificate=true;Initial Catalog={CompanyDB}"
if ($SimulateLegacy) { $env:B1McpCompat__SimulateLegacy = 'true' }
# Release/build of the Service Layer are declared, never detected (compat/README.md, 'Versions and builds'). Without them the connector refuses unless -AllowUnsupportedBuild.
if ($Release) { $env:B1McpCompat__Release = $Release }
if ($Build) { $env:B1McpCompat__Build = $Build }
if ($AllowUnsupportedBuild) { $env:B1McpCompat__AllowUnsupportedBuild = 'true' }

Write-Host "Running profile '$Name' on $($env:B1McpCompat__Listen). Ctrl+C to stop."
dotnet watch run --project (Join-Path $root 'compat\src\B1.Mcp.Compat.Host')
