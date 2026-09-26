# Probes the Service Layer surface that the SAP B1 MCP server needs. Read-only apart from Login/Logout.
# Reads NO customer business data: the only data calls return a document number (DocEntry) or an aggregate over it.
#   .\01-service-layer.ps1 -SlUrl https://server:50000 -Company SBODEMO -User manager -OutDir .\out\sl
# Password: env B1_PASSWORD or interactive prompt.
param(
  [Parameter(Mandatory)][string]$SlUrl, [Parameter(Mandatory)][string]$Company, [Parameter(Mandatory)][string]$User,
  [string]$OutDir = '.\out\sl', [bool]$SkipTlsVerify = $true
)
. (Join-Path $PSScriptRoot '..\lib\Common.ps1')
Initialize-Network -SkipTlsVerify $SkipTlsVerify
$OutDir = New-OutDir $OutDir
$SlUrl = $SlUrl.TrimEnd('/'); $b = "$SlUrl/b1s"
$pwd_ = Get-PlainPassword 'B1_PASSWORD' "Service Layer password for $User"
$script:ck = ''
function Hdr($extra = @{}) { $h = @{}; if ($script:ck) { $h['Cookie'] = $script:ck }; foreach ($k in $extra.Keys) { $h[$k] = $extra[$k] }; $h }
$sum = [ordered]@{ collectedAt = (Get-Date).ToString('s'); slUrl = $SlUrl; checks = [ordered]@{} }
function Check($name, $res, $extra = @{}) {
  $e = [ordered]@{ status = $res.Status; ms = $res.Ms; bytes = $res.Bytes.Length; error = (Get-SlErrorMessage $res) }
  foreach ($k in $extra.Keys) { $e[$k] = $extra[$k] }
  $sum.checks[$name] = $e
}

# 1. Login v1 + v2 (v2 is mandatory for the MCP)
$loginBody = (@{ CompanyDB = $Company; UserName = $User; Password = $pwd_ } | ConvertTo-Json -Compress)
$l1 = Invoke-Http POST "$b/v1/Login" $loginBody ; Save-Call $OutDir '01_login_v1' $l1 -Redact
$l2 = Invoke-Http POST "$b/v2/Login" $loginBody; $script:ck = Get-CookieHeader $l2; Save-Call $OutDir '01_login_v2' $l2 -Redact
$ver = [regex]::Match($l2.Text, '"Version"\s*:\s*"?(\d+)"?').Groups[1].Value
Check 'login_v1' $l1; Check 'login_v2' $l2 @{ b1Version = $ver }
if ($l2.Status -ne 200) { Write-Result FAIL "Login /b1s/v2 failed (HTTP $($l2.Status)): $(Get-SlErrorMessage $l2)"; Save-Json $sum "$OutDir\sl-summary.json"; exit 2 }
Write-Result PASS "Login /b1s/v2 OK. B1 version code: $ver (1000260 = 10.0 FP2408, 1000340 = FP2608)"

# 2. Metadata: full v2, plus the FP2608 scoped/annotated variants (older SL is expected to ignore the options)
$full = Invoke-Http GET "$b/v2/`$metadata" -Headers (Hdr @{ Accept = 'application/xml' })
[IO.File]::WriteAllBytes((Join-Path $OutDir 'md-v2-full.xml'), $full.Bytes)
$list = Invoke-Http GET "$b/v2/`$metadata?scope=entityset&annotation=labelWithTable" -Headers (Hdr @{ Accept = 'application/xml' })
$ord = Invoke-Http GET "$b/v2/`$metadata?scope=entityset&annotation=labelWithField&annotation=labelWithTable&entityset=Orders&dependency=true" -Headers (Hdr @{ Accept = 'application/xml' })
foreach ($p in @(@('02_md_v2_scoped_list', $list), @('02_md_v2_scoped_orders', $ord))) { Set-Content "$OutDir\$($p[0]).headers.txt" "HTTP $($p[1].Status) bytes=$($p[1].Bytes.Length)" }
$entitySets = @([regex]::Matches($full.Text, '<EntitySet [^>]*Name="([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
$annotated = $list.Text.Contains('SAPB1.TableName')
Check 'metadata_v2_full' $full @{ entitySets = $entitySets.Count; annotated = $full.Text.Contains('SAPB1.TableName') }
Check 'metadata_v2_scoped_list' $list @{ sameSizeAsFull = ($list.Bytes.Length -eq $full.Bytes.Length); annotated = $annotated }
Check 'metadata_v2_scoped_orders' $ord @{ sameSizeAsFull = ($ord.Bytes.Length -eq $full.Bytes.Length); annotated = $ord.Text.Contains('SAPB1.ColumnName') }
Write-Result INFO "Entity sets in /b1s/v2/`$metadata: $($entitySets.Count)  (user tables/objects: $(@($entitySets | Where-Object { $_ -like 'U_*' }).Count) with U_ prefix)"
if ($annotated) { Write-Result INFO 'Scoped metadata with annotations is supported natively (this is FP2608 or later).' }
elseif ($list.Bytes.Length -eq $full.Bytes.Length) { Write-Result WARN 'Scoped $metadata options ignored (same size as full). Compat connector is required.' }
else { Write-Result WARN "Scoped metadata differs from full but has no annotations (size $($list.Bytes.Length) vs $($full.Bytes.Length))." }

# 3. Personal data classification endpoints used by the MCP
$pf = Invoke-Http GET "$b/v2/PersonalFieldsSetups?`$top=1" -Headers (Hdr); Save-Call $OutDir '03_personalfields_set' $pf
$pfc = Invoke-Http GET "$b/v2/PersonalFieldsSetups/`$count" -Headers (Hdr); Save-Call $OutDir '03_personalfields_count' $pfc
$pfb = Invoke-Http POST "$b/v2/PersonalFieldsSetupsService_GetPersonalFieldsByTable" '{"PersonalFieldsSetupTableParams":{"TableName":"OCRD"}}' -Headers (Hdr)
[IO.File]::WriteAllText("$OutDir\03_personalfields_bytable.summary.txt", "HTTP $($pfb.Status) bytes=$($pfb.Bytes.Length) rows=$(([regex]::Matches($pfb.Text,'"FieldName"')).Count)")
Check 'personalfields_set' $pf; Check 'personalfields_count' $pfc; Check 'personalfields_bytable' $pfb @{ rows = ([regex]::Matches($pfb.Text, '"FieldName"')).Count }
if ($pfb.Status -eq 200) { Write-Result PASS 'PersonalFieldsSetupsService_GetPersonalFieldsByTable available.' } else { Write-Result WARN "GetPersonalFieldsByTable not available (HTTP $($pfb.Status): $(Get-SlErrorMessage $pfb)). Connector must synthesize it." }

# 4. OData features the MCP uses (data touched: DocEntry numbers only)
$ap = Invoke-Http GET "$b/v2/Orders?`$apply=groupby((DocType),aggregate(DocEntry%20with%20max%20as%20M))&`$top=2" -Headers (Hdr); Save-Call $OutDir '04_apply' $ap
$mp = Invoke-Http GET "$b/v2/Orders?`$select=DocEntry&`$top=1" -Headers (Hdr @{ Prefer = 'odata.maxpagesize=1' }); Save-Call $OutDir '04_maxpagesize' $mp
$cn = Invoke-Http GET "$b/v2/Orders/`$count" -Headers (Hdr); Save-Call $OutDir '04_count' $cn
$sd = Invoke-Http GET "$b/v2/" -Headers (Hdr); Save-Call $OutDir '04_service_document' $sd
Check 'odata_apply' $ap; Check 'odata_maxpagesize' $mp @{ nextLink = $mp.Text.Contains('nextLink') }; Check 'odata_count' $cn
foreach ($t in @(@('$apply', $ap), @('Prefer odata.maxpagesize', $mp), @('$count', $cn))) { if ($t[1].Status -eq 200) { Write-Result PASS "$($t[0]) works" } else { Write-Result WARN "$($t[0]) -> HTTP $($t[1].Status): $(Get-SlErrorMessage $t[1])" } }

# 5. Authentication behaviours the MCP relies on (OAuth bearer support is informational: the first version is direct mode only)
$na = Invoke-Http GET "$b/v2/Items?`$top=1"; Check 'no_auth' $na
$fb = Invoke-Http GET "$b/v2/Items?`$top=1" -Headers @{ Authorization = 'Bearer a.b.c'; 'x-b1-companyID' = '1' }; Check 'fake_bearer' $fb
Write-Result INFO "No session -> HTTP $($na.Status); fake bearer -> HTTP $($fb.Status) ($(Get-SlErrorMessage $fb))"

# 6. Logout
$lo = Invoke-Http POST "$b/v2/Logout" '' -Headers (Hdr); Check 'logout' $lo
Save-Json $sum "$OutDir\sl-summary.json"
Write-Result INFO "Service Layer probe saved in $OutDir"
