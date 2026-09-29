# Saves the connection details of one installation, encrypted for your Windows user, in .dev\<Name>.profile (ignored by git).
# Running it again asks everything from scratch and overwrites that profile.
#   .\setup.ps1                 # profile "default"
#   .\setup.ps1 -Name cliente2208
param([string]$Name = 'default')
$ErrorActionPreference = 'Stop'
function Ask($q, $def = '') { $a = Read-Host $(if ($def) { "$q [$def]" } else { $q }); if ($a) { $a } else { $def } }
function AskSecret($q) { $s = Read-Host $q -AsSecureString; [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)) }

$p = [ordered]@{
  SlUrl     = Ask 'Service Layer URL' 'https://localhost:50000'
  Company   = Ask 'Company database (CompanyDB)'
  User      = Ask 'Service Layer user' 'manager'
  Password  = AskSecret 'Service Layer password'
  SqlHost   = Ask 'SQL Server host' 'localhost'
  SqlWindowsAuth = (Ask 'SQL login with your Windows identity? (y/n)' 'y') -eq 'y'
}
if (-not $p.SqlWindowsAuth) { $p.SqlUser = Ask 'SQL user'; $p.SqlPassword = AskSecret 'SQL password' }

$dir = Join-Path $PSScriptRoot '..\..\.dev'
New-Item -ItemType Directory -Force $dir | Out-Null
$file = Join-Path $dir "$Name.profile"
# ConvertFrom-SecureString without a key encrypts with DPAPI: only this Windows user on this machine can read it back.
ConvertTo-SecureString ($p | ConvertTo-Json) -AsPlainText -Force | ConvertFrom-SecureString | Set-Content $file
Write-Host "Saved $file"
