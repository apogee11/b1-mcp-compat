# Shared by the dev scripts (dot-sourced). Credentials live in plain text in .credentials\<Name>.json, which git ignores.
# Local development only: never copy that folder to a server, a ticket or a chat.
function Get-DevCredentials([string]$Name) {
  $file = Join-Path $PSScriptRoot "..\..\.credentials\$Name.json"
  if (-not (Test-Path $file)) { throw "No credentials '$Name'. Run tools\dev\init-credentials.ps1 -Name $Name, then fill in .credentials\$Name.json." }
  $c = Get-Content $file -Raw | ConvertFrom-Json
  $left = @($c.PSObject.Properties | Where-Object { "$($_.Value)" -like 'REPLACE_*' } | ForEach-Object Name)
  if ($left) { throw "Fill in .credentials\$Name.json first; still a placeholder: $($left -join ', ')." }
  $c
}
