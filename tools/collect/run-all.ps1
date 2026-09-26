# Runs the three probes and zips the result. Run it from any machine that can reach the Service Layer and the SQL server.
#   .\run-all.ps1 -SlUrl https://server:50000 -Company SBODEMO -User manager -SqlServer SQLHOST
# Optional: -Database (defaults to -Company), -SqlUser (SQL login instead of Windows identity), -OutRoot
param(
  [Parameter(Mandatory)][string]$SlUrl, [Parameter(Mandatory)][string]$Company, [Parameter(Mandatory)][string]$User,
  [Parameter(Mandatory)][string]$SqlServer, [string]$Database = '', [string]$SqlUser = '', [string]$OutRoot = '.\out'
)
if (-not $Database) { $Database = $Company }
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$root = New-OutDir (Join-Path $OutRoot "probe-$env:COMPUTERNAME-$stamp")
Write-Host "== 1/3 Service Layer"; & (Join-Path $PSScriptRoot '01-service-layer.ps1') -SlUrl $SlUrl -Company $Company -User $User -OutDir (Join-Path $root 'sl')
Write-Host "`n== 2/3 Database catalog"; & (Join-Path $PSScriptRoot '02-database.ps1') -SqlServer $SqlServer -Database $Database -SqlUser $SqlUser -OutDir (Join-Path $root 'db')
Write-Host "`n== 3/3 Cross-check"
if ((Test-Path "$root\sl\md-v2-full.xml") -and (Test-Path "$root\db\db-summary.json")) { & (Join-Path $PSScriptRoot '03-cross-check.ps1') -SlDir "$root\sl" -DbDir "$root\db" -OutDir "$root\cross" } else { Write-Result WARN 'Skipped: a previous step produced no output.' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = "$root.zip"; if (Test-Path $zip) { Remove-Item $zip }
[IO.Compression.ZipFile]::CreateFromDirectory($root, $zip)
Write-Host "`nSend this file: $zip"
Write-Host 'It contains Service Layer metadata (names of entities/fields, incl. user-defined), database catalog names and feature checks. No business rows, no passwords, no session ids.'
