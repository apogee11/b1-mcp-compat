# Checks the connection settings of the connector (the B1McpCompat__ variables of README 4.2 to 4.4) the way the service will use them.
# Read-only: it opens a SQL connection and sends one GET to the Service Layer. No business data is read, nothing is written or installed.
#   .\check-connection.ps1 -Vars $vars
# - SQL login (B1McpCompat__Database__User set): tries that login. The Windows account does not matter.
# - Windows authentication (no SQL user): tries it AS the service account (-Account), a gMSA included, through a one-shot scheduled task,
#   because the service will connect as that account and not as whoever runs this script. It asks for the account (and for its password
#   unless it is a gMSA) only in this case; press Enter at the account prompt to try the current user instead. -Account/-Password skip the prompts.
# - Service Layer: tries B1McpCompat__ServiceLayer__Url honouring B1McpCompat__ServiceLayer__SkipTlsVerify. Any HTTP answer (even 401) proves
#   the address works; with SkipTlsVerify=false an untrusted certificate fails, as it would for the connector.
# Exits with an error if something fails, so a calling snippet stops there.
param(
  [Parameter(Mandatory)][string[]]$Vars,
  [string]$Account = '',
  [string]$Password = ''
)
$ErrorActionPreference = 'Stop'
$p = 'B1McpCompat__'
$cfg = @{}
foreach ($line in $Vars) { if ($line) { $k, $v = $line -split '=', 2; $cfg[$k] = $v } }
function Setting([string]$name, [string]$default = '') { $v = $cfg[$p + $name]; if ($v) { $v } else { $default } }

$failed = 0
function Ok([string]$m)   { Write-Host "[ OK ] $m" -ForegroundColor Green }
function Fail([string]$m) { Write-Host "[FAIL] $m" -ForegroundColor Red; $script:failed++ }

# ---- SQL Server
$server = Setting 'Database__Server'
$sqlUser = Setting 'Database__User'
$trust = (Setting 'Database__TrustServerCertificate' 'false')
if (-not $server) { Fail 'SQL Server: B1McpCompat__Database__Server is not set (4.3).' }
else {
  $cs = "Server=$server;Database=master;TrustServerCertificate=$trust;Connect Timeout=5;"
  if ($sqlUser) {
    try {
      $c = New-Object System.Data.SqlClient.SqlConnection ($cs + "User ID=$sqlUser;Password=$(Setting 'Database__Password')")
      $c.Open(); $c.Close()
      Ok "SQL Server $server : login '$sqlUser' works."
    } catch { Fail "SQL Server $server : login '$sqlUser' failed (4.2/4.3): $($_.Exception.Message)" }
  } else {
    $cs += 'Integrated Security=True'
    if (-not $Account) { $Account = Read-Host 'Windows authentication: service account of 4.1 (e.g. CORP\svc or CORP\gmsa$), Enter to use the current user' }
    if ($Account -and -not $Account.EndsWith('$') -and -not $Password) {
      $Password = [Net.NetworkCredential]::new('', (Read-Host -AsSecureString "Password for $Account")).Password
    }
    $current = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    if (-not $Account -or $Account -ieq $current) {
      try {
        $c = New-Object System.Data.SqlClient.SqlConnection $cs
        $c.Open(); $cmd = $c.CreateCommand(); $cmd.CommandText = 'SELECT SUSER_SNAME()'; $who = $cmd.ExecuteScalar(); $c.Close()
        Ok "SQL Server $server : Windows authentication works as $who."
      } catch { Fail "SQL Server $server : Windows authentication as $current failed (4.1/4.2): $($_.Exception.Message)" }
    } else {
      # run the same test as the service account through a one-shot scheduled task
      $dir = Join-Path $env:TEMP ('b1compat-' + [guid]::NewGuid().ToString('N'))
      $task = 'b1-mcp-compat-check-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
      New-Item -ItemType Directory $dir | Out-Null
      try {
        & icacls $dir /grant "${Account}:(OI)(CI)M" | Out-Null
        $probe = Join-Path $dir 'probe.ps1'; $result = Join-Path $dir 'result.txt'
        @"
try {
  `$c = New-Object System.Data.SqlClient.SqlConnection '$cs'
  `$c.Open(); `$cmd = `$c.CreateCommand(); `$cmd.CommandText = 'SELECT SUSER_SNAME()'
  'OK ' + `$cmd.ExecuteScalar()
} catch { 'FAIL ' + `$_.Exception.Message } | Set-Content '$result'
"@ | Set-Content $probe
        $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$probe`""
        if ($Account.EndsWith('$')) {
          Register-ScheduledTask $task -Action $action -Principal (New-ScheduledTaskPrincipal -UserId $Account -LogonType Password) | Out-Null
        } else {
          Register-ScheduledTask $task -Action $action -User $Account -Password $Password | Out-Null
        }
        Start-ScheduledTask $task
        for ($i = 0; $i -lt 30 -and -not (Test-Path $result); $i++) { Start-Sleep 1 }
        $out = if (Test-Path $result) { (Get-Content $result -Raw).Trim() } else { 'FAIL the task did not answer in 30 s (does the account have the Log on as a batch job right?)' }
        if ($out -like 'OK *') { Ok "SQL Server $server : Windows authentication works as $($out.Substring(3))." }
        else { Fail "SQL Server $server : Windows authentication as $Account failed (4.1/4.2): $($out -replace '^FAIL ', '')" }
      } catch { Fail "SQL Server $server : could not test as $Account : $($_.Exception.Message)" }
      finally {
        Unregister-ScheduledTask $task -Confirm:$false -ErrorAction SilentlyContinue
        Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
      }
    }
  }
}

# ---- Service Layer
$slUrl = Setting 'ServiceLayer__Url'
if (-not $slUrl) { Fail 'Service Layer: B1McpCompat__ServiceLayer__Url is not set (4.4).' }
else {
  $skip = (Setting 'ServiceLayer__SkipTlsVerify' 'false') -eq 'true'
  [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
  [Net.ServicePointManager]::ServerCertificateValidationCallback = if ($skip) { { $true } } else { $null }
  $tls = if ($skip) { 'certificate not verified' } else { 'certificate verified' }
  try {
    Invoke-WebRequest ($slUrl.TrimEnd('/') + '/b1s/v2/') -UseBasicParsing -TimeoutSec 10 | Out-Null
    Ok "Service Layer $slUrl answers ($tls)."
  } catch {
    if ($_.Exception.Response) { Ok "Service Layer $slUrl answers HTTP $([int]$_.Exception.Response.StatusCode) ($tls)." }
    else { Fail "Service Layer $slUrl : $($_.Exception.Message) ($tls; 4.4)" }
  }
}

if ($failed) { throw "$failed check(s) failed. Fix the settings above before creating the service." }
Write-Host 'All connection checks passed.' -ForegroundColor Green
