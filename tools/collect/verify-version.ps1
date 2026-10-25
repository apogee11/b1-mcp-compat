# Verification of an OLD Service Layer (2208 / 2408) against the connector's KB. READ-ONLY (Login/Logout apart). PowerShell 5.1.
# Collects: login + Version code, FULL flat $metadata (sets, types, properties with types), InventoryGen* probe,
# UserFieldsMD/UserTablesMD/UserObjectsMD counts (reuses check-upstream.ps1), and with -SqlServer: CUFD/OUTB/OUDO/OPFS counts and
# the table/column list of the company DB (INFORMATION_SCHEMA, structure only). No business rows, no field values.
# Passwords: $env:B1_PASSWORD / $env:SQL_PASSWORD or a secure prompt. Never on the command line, never written to disk.
#   .\verify-version.ps1 -SlUrl https://HOST:50000 -Company DB -User manager -Tag 2208 -SqlServer HOST -SqlAuth Sql -SqlUser sa
#   .\verify-version.ps1 -SlUrl https://HOST:50000 -Company DB -User manager -Tag 2408 -SqlServer HOST\INST -SqlAuth Windows
param(
  [Parameter(Mandatory)][string]$SlUrl, [string]$Company, [Parameter(Mandatory)][string]$User,
  [Parameter(Mandatory)][ValidateSet('2208', '2408')][string]$Tag,
  [string]$SqlServer = '', [ValidateSet('Sql', 'Windows')][string]$SqlAuth = 'Windows', [string]$SqlUser = '',
  [string]$OutDir = '', [bool]$SkipTlsVerify = $true
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')
Initialize-Network -SkipTlsVerify $SkipTlsVerify
if (-not $OutDir) { $OutDir = ".\out\verify-$Tag" }
$OutDir = New-OutDir $OutDir
if (-not $Company) { $Company = Read-Host 'Company database (CompanyDB)' }
if ($SqlServer -and $SqlAuth -eq 'Sql' -and -not $SqlUser) { throw '-SqlAuth Sql requires -SqlUser' }
$SlUrl = $SlUrl.TrimEnd('/')
$slPwd = Get-PlainPassword 'B1_PASSWORD' "Service Layer password for $User"
$env:B1_PASSWORD = $slPwd   # process scope only, so check-upstream.ps1 does not prompt again; removed at the end
$sqlPwd = ''
if ($SqlServer -and $SqlAuth -eq 'Sql') { $sqlPwd = Get-PlainPassword 'SQL_PASSWORD' "SQL password for $SqlUser" }

$sb = New-Object Text.StringBuilder
function Log([string]$m) { Write-Host $m; [void]$sb.AppendLine($m) }
$res = [ordered]@{ tag = $Tag; company = $Company; slUrl = $SlUrl; collectedAt = (Get-Date).ToString('s'); host = $env:COMPUTERNAME }
$script:ck = ''
function Hdr($extra = @{}) { $h = @{}; if ($script:ck) { $h['Cookie'] = $script:ck }; foreach ($k in $extra.Keys) { $h[$k] = $extra[$k] }; $h }

Log "# Verification $Tag - $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) - $SlUrl company $Company"

# 1. Login (v2) and Version
Log ''; Log '## 1. Login'
$base = $null; $ver = ''
$body = (@{ CompanyDB = $Company; UserName = $User; Password = $slPwd } | ConvertTo-Json -Compress)
$l = Invoke-Http POST "$SlUrl/b1s/v2/Login" $body
Log "- POST /b1s/v2/Login -> HTTP $($l.Status)$(if ($l.Status -ne 200) { ' ' + (Get-SlErrorMessage $l) })"
if ($l.Status -eq 200) { $base = "$SlUrl/b1s/v2"; $script:ck = Get-CookieHeader $l; $ver = [regex]::Match($l.Text, '"Version"\s*:\s*"?(\d+)"?').Groups[1].Value }
$slPwd = $null
if (-not $base) { $env:B1_PASSWORD = $null; Write-Result FAIL 'Login failed on v2'; Set-Content "$OutDir\report.md" $sb.ToString() -Encoding UTF8; exit 2 }
Log "- Version returned by Login: $ver (expected 1000200 for 2208, 1000260 for 2408)"
$res['loginBase'] = $base; $res['versionCode'] = $ver

# 2. Flat $metadata, complete
Log ''; Log '## 2. Flat $metadata'
$m = Invoke-Http GET "$base/`$metadata" -Headers (Hdr @{ Accept = 'application/xml' })
[IO.File]::WriteAllBytes((Join-Path $OutDir 'metadata-flat.xml'), $m.Bytes)
Log "- GET `$metadata -> HTTP $($m.Status), $($m.Bytes.Length) bytes"
$metaSetNames = @()
if ($m.Status -eq 200) {
  $xml = [xml]$m.Text
  $setRows = @($xml.SelectNodes("//*[local-name()='EntitySet']") | ForEach-Object { '{0},{1}' -f $_.GetAttribute('Name'), $_.GetAttribute('EntityType') })
  $metaSetNames = @($xml.SelectNodes("//*[local-name()='EntitySet']") | ForEach-Object { $_.GetAttribute('Name') } | Sort-Object)
  Set-Content "$OutDir\entitysets.txt" $metaSetNames -Encoding UTF8
  Set-Content "$OutDir\entitysets.csv" (@('EntitySet,EntityType') + $setRows) -Encoding UTF8
  $props = New-Object Collections.ArrayList
  foreach ($et in $xml.SelectNodes("//*[local-name()='EntityType']")) {
    foreach ($p in $et.SelectNodes("*[local-name()='Property']")) { [void]$props.Add(('{0},{1},{2}' -f $et.GetAttribute('Name'), $p.GetAttribute('Name'), $p.GetAttribute('Type'))) }
  }
  Set-Content "$OutDir\entitytypes-properties.csv" (@('EntityType,Property,Type') + $props) -Encoding UTF8
  Log "- entity sets: $($metaSetNames.Count); properties: $($props.Count)"
  $res['metadata'] = [ordered]@{ entitySets = $metaSetNames.Count; properties = $props.Count }
}

# 3. InventoryGen* probe (route + $metadata)
Log ''; Log '## 3. InventoryGenEntries / InventoryGenExits (new names) vs InventoryGenEntry / InventoryGenExit (old)'
$probe = [ordered]@{}
foreach ($n in 'InventoryGenEntries', 'InventoryGenExits', 'InventoryGenEntry', 'InventoryGenExit') {
  $row = [ordered]@{ inMetadataSets = ($metaSetNames -contains $n) }
  $row['http_v2'] = (Invoke-Http GET "$base/${n}?`$top=0" -Headers (Hdr)).Status
  $probe[$n] = $row
  Log "- ${n}: in `$metadata sets=$($row.inMetadataSets) GET v2=$($row.http_v2)"
}
$res['inventoryGen'] = $probe

# 4. UDF/UDT/UDO catalog (reuses check-upstream.ps1; it logs in on its own using $env:B1_PASSWORD)
Log ''; Log '## 4. UserFieldsMD / UserTablesMD / UserObjectsMD (check-upstream.ps1)'
try {
  & (Join-Path $PSScriptRoot 'check-upstream.ps1') -SlUrl $SlUrl -Company $Company -User $User -Tag $Tag -OutDir (Join-Path $OutDir 'check-upstream') -SkipTlsVerify $SkipTlsVerify | Out-Null
  $cu = Join-Path $OutDir 'check-upstream\results.json'
  if (Test-Path $cu) {
    $c = Get-Content $cu -Raw | ConvertFrom-Json
    foreach ($k in 'UserFieldsMD', 'UserTablesMD', 'UserObjectsMD') { Log "- $k rows=$($c.catalog.$k.rows) HTTP $($c.catalog.$k.status)" }
    $res['catalog'] = $c.catalog
  }
} catch { Log "- check-upstream failed: $($_.Exception.Message)" }
$env:B1_PASSWORD = $null

[void](Invoke-Http POST "$base/Logout" '{}' -Headers (Hdr))

# 5. SQL (structure + counts only)
$res['sql'] = $null
if ($SqlServer) {
  Log ''; Log '## 5. SQL structure and counts'
  try {
    $cs = if ($SqlAuth -eq 'Sql') { "Server=$SqlServer;Database=$Company;User ID=$SqlUser;Password=$sqlPwd;TrustServerCertificate=True" } else { "Server=$SqlServer;Database=$Company;Integrated Security=True;TrustServerCertificate=True" }
    $cn = New-Object Data.SqlClient.SqlConnection($cs); $cn.Open(); $sqlPwd = $null
    function Q([string]$t) { $c = $cn.CreateCommand(); $c.CommandText = $t; $c.CommandTimeout = 300; $dt = New-Object Data.DataTable; $dt.Load($c.ExecuteReader()); , $dt }
    $sqlRes = [ordered]@{}
    foreach ($t in 'CUFD', 'OUTB', 'OUDO', 'OPFS') {
      $ex = (Q "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='$t'").Rows[0][0]
      $cnt = if ($ex -gt 0) { (Q "SELECT COUNT(*) FROM [$t]").Rows[0][0] } else { $null }
      $sqlRes[$t] = [ordered]@{ exists = ($ex -gt 0); rows = $cnt }
      Log "- ${t}: exists=$($ex -gt 0) rows=$cnt"
    }
    $cols = Q "SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE FROM INFORMATION_SCHEMA.COLUMNS ORDER BY TABLE_NAME, ORDINAL_POSITION"
    $cols.Rows | Select-Object TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE | Export-Csv "$OutDir\sql-columns.csv" -NoTypeInformation -Encoding UTF8
    Log "- sql-columns.csv: $($cols.Rows.Count) columns in $(@($cols.Rows | ForEach-Object { $_.TABLE_NAME } | Sort-Object -Unique).Count) tables"
    $cn.Close(); $res['sql'] = $sqlRes
  } catch { Log "- SQL failed: $($_.Exception.Message)"; $res['sql'] = [ordered]@{ error = $_.Exception.Message } }
}

Set-Content "$OutDir\report.md" $sb.ToString() -Encoding UTF8
Save-Json $res "$OutDir\results.json"
Write-Result PASS "Done. Return the folder: $OutDir"
