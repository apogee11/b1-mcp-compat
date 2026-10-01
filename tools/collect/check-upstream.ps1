# Checks that an older Service Layer gives the connector what it needs: the user-defined catalog (UDF / UDT / UDO) readable through the
# Service Layer itself (UserFieldsMD, UserTablesMD, UserObjectsMD), the plain $metadata carrying the user fields, and (optional) OPFS readable.
# Read-only apart from Login/Logout. Reads field/table/object DEFINITIONS only, no business data.
#   .\check-upstream.ps1 -SlUrl https://SRVSAP01:50000 -Company SBODEMO -User manager -Tag 2408
# Password: env B1_PASSWORD or interactive prompt. Optional OPFS read: add -SqlServer (Windows identity, or -SqlUser + env B1_SQL_PASSWORD).
# Output: .\out\check-<Tag>\ (report.md, results.json, raw dumps). Nothing secret is written.
param(
  [Parameter(Mandatory)][string]$SlUrl, [string]$Company, [Parameter(Mandatory)][string]$User,
  [string]$Tag = '', [string]$OutDir = '', [bool]$SkipTlsVerify = $true,
  [string]$SlPath = 'C:\Program Files\SAP\SAP Business One ServerTools\ServiceLayer',
  [string]$SqlServer = '', [string]$SqlUser = ''
)
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')
Initialize-Network -SkipTlsVerify $SkipTlsVerify
if (-not $OutDir) { $OutDir = ".\out\check" + $(if ($Tag) { "-$Tag" } else { '' }) }
$OutDir = New-OutDir $OutDir
if (-not $Company) { $Company = Read-Host 'Company database (CompanyDB)' }
$SlUrl = $SlUrl.TrimEnd('/'); $b = "$SlUrl/b1s/v2"
$pwd_ = Get-PlainPassword 'B1_PASSWORD' "Service Layer password for $User"

$sb = New-Object Text.StringBuilder
function Log([string]$m) { Write-Host $m; [void]$sb.AppendLine($m) }
function Sample($a, [int]$n = 15) { @($a) | Select-Object -First $n }
$R = [ordered]@{ tag = $Tag; company = $Company; slUrl = $SlUrl; collectedAt = (Get-Date).ToString('s'); host = $env:COMPUTERNAME }
$script:ck = ''
function Hdr($extra = @{}) { $h = @{}; if ($script:ck) { $h['Cookie'] = $script:ck }; foreach ($k in $extra.Keys) { $h[$k] = $extra[$k] }; $h }

# GET every page of a collection (follows nextLink). Returns Ok/Status/Items/Pages/Error.
function Get-All([string]$Name) {
  $items = New-Object Collections.ArrayList; $url = "$b/$Name"; $pages = 0
  while ($url) {
    $r = Invoke-Http GET $url -Headers (Hdr @{ Prefer = 'odata.maxpagesize=1000' })
    if ($r.Status -ne 200) { return [pscustomobject]@{ Ok = $false; Status = $r.Status; Items = $items; Pages = $pages; Error = (Get-SlErrorMessage $r) } }
    $j = $r.Text | ConvertFrom-Json
    if ($j.PSObject.Properties['value'] -and $j.value) { [void]$items.AddRange(@($j.value)) }
    $pages++
    $m = [regex]::Match($r.Text, '"@?odata\.nextLink"\s*:\s*"([^"]+)"')
    if ($m.Success) {
      $n = $m.Groups[1].Value
      $url = if ($n -match '^https?://') { $n } elseif ($n.StartsWith('/b1s/')) { "$SlUrl$n" } else { "$b/" + $n.TrimStart('/') }
    } else { $url = $null }
  }
  [pscustomobject]@{ Ok = $true; Status = 200; Items = $items; Pages = $pages; Error = $null }
}

Log "# Service Layer check - $Tag"
Log "Collected $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) on $env:COMPUTERNAME against $SlUrl, company $Company"
Log ''

# 0. Binaries (only when the Service Layer is installed on this machine)
Log '## 0. Installed binaries'
$bins = @()
foreach ($f in 'OBServerDLL.dll', 'B1_Engine.dll', 'B1_DIInternalFields100.dll', 'ServiceLayer.dll') {
  $p = Join-Path $SlPath $f
  if (Test-Path $p) {
    $i = Get-Item $p; $h = (Get-FileHash $p -Algorithm SHA256).Hash
    $bins += [ordered]@{ file = $f; version = $i.VersionInfo.FileVersion; bytes = $i.Length; sha256 = $h }
    Log "- $f version=$($i.VersionInfo.FileVersion) bytes=$($i.Length) sha256=$h"
  } else { Log "- ${f}: not found under $SlPath (run on the Service Layer host, or pass -SlPath)" }
}
$R['binaries'] = $bins
Log ''

# 1. Login
Log '## 1. Login'
$loginBody = (@{ CompanyDB = $Company; UserName = $User; Password = $pwd_ } | ConvertTo-Json -Compress)
$l = Invoke-Http POST "$b/Login" $loginBody
$script:ck = Get-CookieHeader $l
$ver = [regex]::Match($l.Text, '"Version"\s*:\s*"?(\d+)"?').Groups[1].Value
Log "- POST /b1s/v2/Login -> HTTP $($l.Status)  B1 version code: $ver"
if ($l.Status -ne 200) {
  Log "- FAILED: $(Get-SlErrorMessage $l)"
  Set-Content "$OutDir\report.md" $sb.ToString() -Encoding UTF8
  Write-Result FAIL "Login failed: $(Get-SlErrorMessage $l)"; exit 2
}
$R['b1VersionCode'] = $ver
Log ''

# 2. Plain $metadata
Log '## 2. Plain $metadata'
$m = Invoke-Http GET "$b/`$metadata" -Headers (Hdr @{ Accept = 'application/xml' })
[IO.File]::WriteAllBytes((Join-Path $OutDir 'metadata-v2-full.xml'), $m.Bytes)
$sets = @([regex]::Matches($m.Text, '<EntitySet [^>]*Name="([^"]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object)
Set-Content "$OutDir\entitysets.txt" $sets -Encoding UTF8
Log "- GET /`$metadata -> HTTP $($m.Status), $($m.Bytes.Length) bytes, $($sets.Count) entity sets (entitysets.txt)"
$has = [ordered]@{}
foreach ($need in 'UserFieldsMD', 'UserTablesMD', 'UserObjectsMD', 'PersonalFieldsSetups') { $has[$need] = ($sets -contains $need); Log "- EntitySet $need present: $($has[$need])" }
$R['metadata'] = [ordered]@{ status = $m.Status; bytes = $m.Bytes.Length; entitySets = $sets.Count; present = $has }
Log ''

# 3. Native FP2608 annotations (expected absent before FP2608)
Log '## 3. Native annotations (expected NOT supported before FP2608)'
$a = Invoke-Http GET "$b/`$metadata?scope=entityset&annotation=labelWithTable&annotation=labelWithField&entityset=BusinessPartners&dependency=true" -Headers (Hdr @{ Accept = 'application/xml' })
$ann = $a.Text.Contains('SAPB1.TableName') -or $a.Text.Contains('Common.Label')
Log "- scoped `$metadata -> HTTP $($a.Status), $($a.Bytes.Length) bytes, has SAPB1/Common.Label annotations: $ann"
$R['annotated'] = [ordered]@{ status = $a.Status; bytes = $a.Bytes.Length; hasAnnotations = $ann }
Log ''

# 4. User catalog through the Service Layer
Log '## 4. UserFieldsMD / UserTablesMD / UserObjectsMD'
$cat = [ordered]@{}
foreach ($n in 'UserFieldsMD', 'UserTablesMD', 'UserObjectsMD') {
  $x = Get-All $n; $cat[$n] = $x
  Log "- $n -> ok=$($x.Ok) HTTP $($x.Status) rows=$($x.Items.Count) pages=$($x.Pages)$(if (-not $x.Ok) { ' error: ' + $x.Error })"
  if ($x.Items.Count -gt 0) {
    Log "  properties: $(($x.Items[0].PSObject.Properties.Name) -join ', ')"
    $x.Items | ConvertTo-Json -Depth 8 | Set-Content "$OutDir\$n.json" -Encoding UTF8
  }
}
$uf = $cat['UserFieldsMD']; $ut = $cat['UserTablesMD']; $uo = $cat['UserObjectsMD']
$R['catalog'] = [ordered]@{}
foreach ($n in $cat.Keys) { $R['catalog'][$n] = [ordered]@{ ok = $cat[$n].Ok; status = $cat[$n].Status; rows = $cat[$n].Items.Count } }
Log ''

# 5. Do the user fields already appear in the plain $metadata?
Log '## 5. User fields (U_*) in the plain $metadata vs UserFieldsMD'
$udf = @()
if ($uf.Ok -and $m.Status -eq 200) {
  $xml = [xml]$m.Text
  foreach ($map in @(@('OCRD', 'BusinessPartner'), @('OITM', 'Item'), @('ORDR', 'Document'))) {
    $tbl = $map[0]; $et = $map[1]
    $inMeta = @($xml.SelectNodes("//*[local-name()='EntityType'][@Name='$et']/*[local-name()='Property']") | ForEach-Object { $_.GetAttribute('Name') } | Where-Object { $_ -like 'U_*' })
    $inCat = @($uf.Items | Where-Object { $_.TableName -eq $tbl } | ForEach-Object { "U_$($_.Name)" })
    $onlyMeta = @($inMeta | Where-Object { $inCat -notcontains $_ }); $onlyCat = @($inCat | Where-Object { $inMeta -notcontains $_ })
    Log "- ${tbl}/${et}: in `$metadata=$($inMeta.Count) in UserFieldsMD=$($inCat.Count) only-metadata=$($onlyMeta.Count) only-catalog=$($onlyCat.Count)"
    if ($onlyMeta) { Log "  only in metadata: $((Sample $onlyMeta) -join ', ')" }
    if ($onlyCat) { Log "  only in catalog: $((Sample $onlyCat) -join ', ')" }
    $udf += [ordered]@{ table = $tbl; entityType = $et; inMetadata = $inMeta.Count; inCatalog = $inCat.Count; onlyMetadata = $onlyMeta.Count; onlyCatalog = $onlyCat.Count }
  }
}
$R['udfInMetadata'] = $udf
Log ''

# 6. Personal data classification (FP2608 action; same body the MCP uses)
Log '## 6. Personal data'
$pfs = Invoke-Http GET "$b/PersonalFieldsSetups?`$top=1" -Headers (Hdr)
$pfb = Invoke-Http POST "$b/PersonalFieldsSetupsService_GetPersonalFieldsByTable" '{"PersonalFieldsSetupTableParams":{"TableName":"OCRD"}}' -Headers (Hdr)
Log "- GET PersonalFieldsSetups?`$top=1 -> HTTP $($pfs.Status) $(if ($pfs.Status -ne 200) { Get-SlErrorMessage $pfs })"
Log "- POST PersonalFieldsSetupsService_GetPersonalFieldsByTable(OCRD) -> HTTP $($pfb.Status) rows=$(([regex]::Matches($pfb.Text,'"FieldName"')).Count) $(if ($pfb.Status -ne 200) { Get-SlErrorMessage $pfb })"
$R['personal'] = [ordered]@{ setupsStatus = $pfs.Status; byTableStatus = $pfb.Status; byTableRows = ([regex]::Matches($pfb.Text, '"FieldName"')).Count }
Log ''

# 7. Optional SQL read of OPFS (the only thing the connector reads from the database)
$R['sql'] = $null
if ($SqlServer) {
  Log '## 7. OPFS (personal data classification) from the database'
  try {
    $sqlPwd = if ($SqlUser) { Get-PlainPassword 'B1_SQL_PASSWORD' "SQL password for $SqlUser" } else { '' }
    $cs = if ($SqlUser) { "Server=$SqlServer;Database=$Company;User ID=$SqlUser;Password=$sqlPwd;TrustServerCertificate=True" } else { "Server=$SqlServer;Database=$Company;Integrated Security=True;TrustServerCertificate=True" }
    $c = New-Object Data.SqlClient.SqlConnection($cs); $c.Open()
    try { $cmd = $c.CreateCommand(); $cmd.CommandText = 'SELECT AbsEntry, TableName, FieldName, RefObjType, Category, OrigType FROM OPFS'; $cmd.CommandTimeout = 120; $op = New-Object Data.DataTable; $op.Load($cmd.ExecuteReader()) } finally { $c.Close() }
    $rows = @($op.Rows | ForEach-Object { [ordered]@{ AbsEntry = $_.AbsEntry; TableName = $_.TableName; FieldName = $_.FieldName; RefObjType = $_.RefObjType; Category = $_.Category; OrigType = $_.OrigType } })
    $rows | ConvertTo-Json -Depth 4 | Set-Content "$OutDir\OPFS.json" -Encoding UTF8
    $byCat = @($rows | Group-Object { "$($_.Category)" } | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ' '
    $nTables = @($rows | ForEach-Object { $_.TableName } | Sort-Object -Unique).Count
    Log "- OPFS: rows=$($rows.Count) tables=$nTables  by Category: $byCat  (OPFS.json)"
    $R['sql'] = [ordered]@{ opfs = [ordered]@{ rows = $rows.Count; tables = $nTables; byCategory = $byCat } }
  } catch { Log "- OPFS not readable: $($_.Exception.Message)"; $R['sql'] = [ordered]@{ error = $_.Exception.Message } }
  Log ''
}

# 8. Verdict
Log '## 8. Verdict'
$ok1 = $uf.Ok -and $ut.Ok -and $uo.Ok
$ok2 = ($udf.Count -gt 0) -and (@($udf | Where-Object { $_.onlyMetadata -gt 0 -or $_.onlyCatalog -gt 0 }).Count -eq 0)
$sqlState = if (-not $R['sql']) { 'N/A' } elseif ($R['sql'].Contains('error')) { 'ERROR' } else { 'OK' }
Log "- [$(if ($ok1) {'OK'} else {'FAIL'})] UserFieldsMD, UserTablesMD and UserObjectsMD are readable through the Service Layer"
Log "- [$(if ($ok2) {'OK'} else {'CHECK'})] User fields in the catalog match the U_* properties of the plain `$metadata"
Log "- [$sqlState] OPFS readable from the database$(if ($sqlState -eq 'ERROR') { ' (SQL connection failed)' } elseif ($sqlState -eq 'N/A') { ' (not checked: pass -SqlServer)' })"
Log "- Native annotations present: $ann (expected False before FP2608)"
$verdict = if ($ok1 -and $ok2 -and $sqlState -ne 'ERROR') { if ($sqlState -eq 'OK') { 'READY' } else { 'READY (OPFS not checked)' } } elseif ($ok1) { 'PARTIAL' } else { 'NOT READY' }
Log ''; Log "**Result: $verdict**"
$R['verdict'] = $verdict

[void](Invoke-Http POST "$b/Logout" '{}' -Headers (Hdr))
Set-Content "$OutDir\report.md" $sb.ToString() -Encoding UTF8
Save-Json $R "$OutDir\results.json"
Write-Result $(if ($verdict -eq 'READY') { 'PASS' } else { 'WARN' }) "Result: $verdict. Output folder: $OutDir"
