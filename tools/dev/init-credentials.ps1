# Creates .credentials\<Name>.json with placeholders. Edit it with your connection details; it is read by dev.ps1, mcp.ps1 and check-credentials.ps1.
# The .credentials folder is ignored by git. It is plain text and meant for local development only: do not share it.
#   .\init-credentials.ps1                 # "development"
#   .\init-credentials.ps1 -Name cliente2208
param([string]$Name = 'development')
$ErrorActionPreference = 'Stop'
$dir = Join-Path $PSScriptRoot '..\..\.credentials'
$file = Join-Path $dir "$Name.json"
if (Test-Path $file) { Write-Host "Already exists, left untouched: $file"; return }
New-Item -ItemType Directory -Force $dir | Out-Null
@'
{
  "SlUrl": "https://localhost:50000",
  "Company": "REPLACE_WITH_COMPANY_DB",
  "User": "REPLACE_WITH_SERVICE_LAYER_USER",
  "Password": "REPLACE_WITH_SERVICE_LAYER_PASSWORD",
  "SqlHost": "localhost",
  "SqlWindowsAuth": true,
  "SqlUser": "",
  "SqlPassword": ""
}
'@ | Set-Content $file -Encoding UTF8
Write-Host "Created $file"
Write-Host "Edit it and replace the REPLACE_* values. If SQL Server does not use your Windows login, set SqlWindowsAuth to false and fill SqlUser and SqlPassword."
Write-Host "Note: .credentials is ignored by git, so it is never committed. It is plain text, intentionally, for local development only. Do not share it."
