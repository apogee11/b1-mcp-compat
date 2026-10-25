# Checks that the details saved by set-credentials.ps1 work: logs in to the Service Layer and opens the SQL Server connection to the company database.
#   .\check-credentials.ps1                 # profile "default"
#   .\check-credentials.ps1 -Name cliente2208
# Exit code 0 when both checks pass, 1 otherwise.
param([string]$Name = 'default')
$ErrorActionPreference = 'Stop'
$file = Join-Path $PSScriptRoot "..\..\.dev\$Name.profile"
if (-not (Test-Path $file)) { throw "No profile '$Name'. Run tools\dev\set-credentials.ps1 -Name $Name first." }
$secure = Get-Content $file | ConvertTo-SecureString
$p = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)) | ConvertFrom-Json

# Service Layer usually runs with a self-signed certificate.
if ($PSVersionTable.PSVersion.Major -lt 6) { [Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }; [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; [Net.ServicePointManager]::Expect100Continue = $false }   # the Service Layer mishandles "Expect: 100-continue" (empty credentials, error 206)
$tls = if ($PSVersionTable.PSVersion.Major -ge 6) { @{ SkipCertificateCheck = $true } } else { @{} }

$failed = $false
function Check($what, [scriptblock]$test) {
  try { & $test; Write-Host "OK   $what" -ForegroundColor Green }
  catch { Write-Host "FAIL $what : $($_.Exception.Message)" -ForegroundColor Red; $script:failed = $true }
}

$base = $p.SlUrl.TrimEnd('/')
Check "Service Layer login ($base, $($p.Company), $($p.User))" {
  $body = @{ CompanyDB = $p.Company; UserName = $p.User; Password = $p.Password } | ConvertTo-Json
  $r = Invoke-WebRequest "$base/b1s/v2/Login" -Method Post -Body $body -ContentType 'application/json' -UseBasicParsing @tls
  # PowerShell does not keep the B1SESSION cookie (malformed Set-Cookie), so B1SESSION and ROUTEID (the node holding the session) go into a session by hand.
  $ws = New-Object Microsoft.PowerShell.Commands.WebRequestSession
  foreach ($kv in (@($r.Headers['Set-Cookie']) -join ',' -split ',' | ForEach-Object { ($_ -split ';')[0].Trim() })) {
    $n, $v = $kv -split '=', 2
    $ws.Cookies.Add((New-Object Net.Cookie $n, $v, '/', ([Uri]$base).Host))
  }
  Invoke-WebRequest "$base/b1s/v2/Logout" -Method Post -WebSession $ws -UseBasicParsing @tls | Out-Null
}

Check "SQL Server connection ($($p.SqlHost), $($p.Company))" {
  $auth = if ($p.SqlWindowsAuth) { 'Integrated Security=true' } else { "User Id=$($p.SqlUser);Password=$($p.SqlPassword)" }
  $c = New-Object System.Data.SqlClient.SqlConnection "Server=$($p.SqlHost);$auth;TrustServerCertificate=true;Initial Catalog=$($p.Company);Connect Timeout=10"
  try { $c.Open() } finally { $c.Dispose() }
}

if ($failed) { exit 1 }
